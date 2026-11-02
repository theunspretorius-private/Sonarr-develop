using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Trakt;
using NzbDrone.Core.ImportLists.Trakt.Popular;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public class RecommendedSeriesService : IProvideRecommendedSeries
    {
        private const string TraktBaseUrl = "https://api.trakt.tv";
        private const int DefaultLimit = 20;
        private const int MaxDegreeOfParallelism = 6;

        private readonly IHttpClient _httpClient;
        private readonly IProvideSeriesInfo _seriesInfo;
        private readonly ISearchForNewSeries _searchProxy;
        private readonly ISeriesService _seriesService;
        private readonly ICached<List<TraktSeriesResource>> _traktListCache;
        private readonly ICached<Series> _seriesMetadataCache;
        private readonly Logger _logger;

        public RecommendedSeriesService(
            IHttpClient httpClient,
            IProvideSeriesInfo seriesInfo,
            ISearchForNewSeries searchProxy,
            ISeriesService seriesService,
            ICacheManager cacheManager,
            Logger logger)
        {
            _httpClient = httpClient;
            _seriesInfo = seriesInfo;
            _searchProxy = searchProxy;
            _seriesService = seriesService;
            _traktListCache = cacheManager.GetCache<List<TraktSeriesResource>>(GetType(), "traktLists");
            _seriesMetadataCache = cacheManager.GetCache<Series>(GetType(), "seriesMetadata");
            _logger = logger;
        }

        public List<Series> GetRecommendedSeries(RecommendationListType listType, Language language)
        {
            var traktShows = _traktListCache.Get(
                listType.ToString(),
                () => FetchTraktRecommendations(listType),
                TimeSpan.FromMinutes(30));

            if (traktShows == null || traktShows.Count == 0)
            {
                _traktListCache.Remove(listType.ToString());
                return new List<Series>();
            }

            var existingSeriesByTvdbId = _seriesService.GetAllSeries()
                .GroupBy(s => s.TvdbId)
                .ToDictionary(g => g.Key, g => g.First());

            var results = traktShows
                .AsParallel()
                .AsOrdered()
                .WithDegreeOfParallelism(MaxDegreeOfParallelism)
                .Select(show => ResolveSeries(show, language, existingSeriesByTvdbId))
                .Where(series => series != null)
                .DistinctBy(series => series.TvdbId)
                .ToList();

            return results;
        }

        private List<TraktSeriesResource> FetchTraktRecommendations(RecommendationListType listType)
        {
            try
            {
                var requestBuilder = new HttpRequestBuilder(TraktBaseUrl)
                    .Accept(HttpAccept.Json)
                    .SetHeader("trakt-api-version", "2")
                    .SetHeader("trakt-api-key", TraktImportBase<TraktPopularSettings>.ClientId)
                    .AddQueryParam("limit", DefaultLimit.ToString(CultureInfo.InvariantCulture));

                switch (listType)
                {
                    case RecommendationListType.Popular:
                        requestBuilder.Resource("/shows/popular");
                        break;
                    case RecommendationListType.New:
                        var currentYear = DateTime.UtcNow.Year;
                        requestBuilder.Resource("/shows/trending")
                                      .AddQueryParam("years", $"{currentYear - 1}-{currentYear}");
                        break;
                    case RecommendationListType.Anticipated:
                        requestBuilder.Resource("/shows/anticipated");
                        break;
                    case RecommendationListType.TopWatched:
                        requestBuilder.Resource("/shows/watched/weekly");
                        break;
                    case RecommendationListType.Trending:
                    default:
                        requestBuilder.Resource("/shows/trending");
                        break;
                }

                var request = requestBuilder.Build();
                request.SuppressHttpError = true;

                var response = _httpClient.Get(request);

                if (response.HasHttpError || response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("Unable to fetch recommended series ({0}) from Trakt. Status: {1}", listType, response.StatusCode);
                    return new List<TraktSeriesResource>();
                }

                var shows = listType == RecommendationListType.Popular
                    ? STJson.Deserialize<List<TraktSeriesResource>>(response.Content)
                    : STJson.Deserialize<List<TraktResponse>>(response.Content)?.SelectList(c => c.Show);

                return shows?.Where(s => s?.Ids != null).ToList() ?? new List<TraktSeriesResource>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to fetch recommended series ({0}) from Trakt.", listType);
                return new List<TraktSeriesResource>();
            }
        }

        private Series ResolveSeries(
            TraktSeriesResource show,
            Language language,
            IReadOnlyDictionary<int, Series> existingSeriesByTvdbId)
        {
            try
            {
                var tvdbId = show.Ids?.Tvdb ?? 0;

                if (tvdbId > 0)
                {
                    if (existingSeriesByTvdbId.TryGetValue(tvdbId, out var existingSeries))
                    {
                        return existingSeries;
                    }

                    return _seriesMetadataCache.Get(
                        $"tvdb_{tvdbId}_{language.Id}",
                        () => _seriesInfo.GetSeriesInfo(tvdbId, language, SeasonType.Official).Item1,
                        TimeSpan.FromHours(1));
                }

                if (show.Ids?.Imdb.IsNotNullOrWhiteSpace() == true)
                {
                    return _seriesMetadataCache.Get(
                        $"imdb_{show.Ids.Imdb}_{language.Id}",
                        () => _searchProxy.SearchForNewSeriesByImdbId(show.Ids.Imdb, language).FirstOrDefault(),
                        TimeSpan.FromHours(1));
                }

                if (show.Ids?.Tmdb is > 0)
                {
                    var tmdbId = show.Ids.Tmdb.Value;

                    return _seriesMetadataCache.Get(
                        $"tmdb_{tmdbId}_{language.Id}",
                        () => _searchProxy.SearchForNewSeriesByTmdbId(tmdbId, language).FirstOrDefault(),
                        TimeSpan.FromHours(1));
                }
            }
            catch (SeriesNotFoundException)
            {
                _logger.Debug("Recommended series '{0}' (TVDB: {1}) was not found on SkyHook.", show.Title, show.Ids?.Tvdb);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to resolve metadata for recommended series '{0}' (TVDB: {1}).", show.Title, show.Ids?.Tvdb);
            }

            return null;
        }
    }
}
