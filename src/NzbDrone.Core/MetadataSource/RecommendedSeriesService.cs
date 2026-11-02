using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
        private const int DefaultLimit = 12;
        private const int MaxConcurrency = 4;

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
            return GetRecommendedSeriesAsync(listType, language).GetAwaiter().GetResult();
        }

        public async Task<List<Series>> GetRecommendedSeriesAsync(
            RecommendationListType listType,
            Language language,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = listType.ToString();
            var traktShows = _traktListCache.Find(cacheKey);

            if (traktShows == null || traktShows.Count == 0)
            {
                traktShows = await FetchTraktRecommendationsAsync(listType, cancellationToken);

                if (traktShows.Count > 0)
                {
                    _traktListCache.Set(cacheKey, traktShows, TimeSpan.FromMinutes(30));
                }
            }

            if (traktShows == null || traktShows.Count == 0)
            {
                return new List<Series>();
            }

            var existingSeriesByTvdbId = GetExistingMatchingSeries(traktShows);
            using var semaphore = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

            var tasks = traktShows.Select(async show =>
            {
                var tvdbId = show.Ids?.Tvdb ?? 0;

                if (tvdbId > 0)
                {
                    if (existingSeriesByTvdbId.TryGetValue(tvdbId, out var existingSeries))
                    {
                        return existingSeries;
                    }

                    var cached = _seriesMetadataCache.Find($"tvdb_{tvdbId}_{language.Id}");
                    if (cached != null)
                    {
                        return cached;
                    }
                }

                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ResolveSeriesAsync(show, language, existingSeriesByTvdbId, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var resolved = await Task.WhenAll(tasks);

            return resolved
                .Where(series => series != null)
                .DistinctBy(series => series.TvdbId)
                .ToList();
        }

        private Dictionary<int, Series> GetExistingMatchingSeries(List<TraktSeriesResource> traktShows)
        {
            var allTvdbIds = _seriesService.AllSeriesTvdbIds();

            if (allTvdbIds == null || allTvdbIds.Count == 0)
            {
                return _seriesService.GetAllSeries()
                    .GroupBy(s => s.TvdbId)
                    .ToDictionary(g => g.Key, g => g.First());
            }

            var candidateTvdbIds = traktShows
                .Select(s => s.Ids?.Tvdb ?? 0)
                .Where(id => id > 0)
                .ToHashSet();

            var matchingSeriesIds = allTvdbIds
                .Where(kvp => candidateTvdbIds.Contains(kvp.Value))
                .Select(kvp => kvp.Key)
                .ToList();

            if (matchingSeriesIds.Count == 0)
            {
                return new Dictionary<int, Series>();
            }

            return _seriesService.GetSeries(matchingSeriesIds)
                .GroupBy(s => s.TvdbId)
                .ToDictionary(g => g.Key, g => g.First());
        }

        private async Task<List<TraktSeriesResource>> FetchTraktRecommendationsAsync(
            RecommendationListType listType,
            CancellationToken cancellationToken)
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

                var response = await _httpClient.GetAsync(request, cancellationToken) ?? _httpClient.Get(request);

                if (response == null || response.HasHttpError || response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("Unable to fetch recommended series ({0}) from Trakt. Status: {1}", listType, response?.StatusCode);
                    return new List<TraktSeriesResource>();
                }

                var shows = listType == RecommendationListType.Popular
                    ? STJson.Deserialize<List<TraktSeriesResource>>(response.Content)
                    : STJson.Deserialize<List<TraktResponse>>(response.Content)?.SelectList(c => c.Show);

                return shows?.Where(s => s?.Ids != null).ToList() ?? new List<TraktSeriesResource>();
            }
            catch (OperationCanceledException)
            {
                return new List<TraktSeriesResource>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to fetch recommended series ({0}) from Trakt.", listType);
                return new List<TraktSeriesResource>();
            }
        }

        private async Task<Series> ResolveSeriesAsync(
            TraktSeriesResource show,
            Language language,
            IReadOnlyDictionary<int, Series> existingSeriesByTvdbId,
            CancellationToken cancellationToken)
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

                    var cacheKey = $"tvdb_{tvdbId}_{language.Id}";
                    var cached = _seriesMetadataCache.Find(cacheKey);
                    if (cached != null)
                    {
                        return cached;
                    }

                    var resolved = await _searchProxy.LookupSeriesByTvdbIdAsync(tvdbId, language, cancellationToken)
                        ?? _seriesInfo.GetSeriesInfo(tvdbId, language, SeasonType.Official)?.Item1;

                    if (resolved != null)
                    {
                        _seriesMetadataCache.Set(cacheKey, resolved, TimeSpan.FromHours(2));
                    }

                    return resolved;
                }

                if (show.Ids?.Imdb.IsNotNullOrWhiteSpace() == true)
                {
                    return _seriesMetadataCache.Get(
                        $"imdb_{show.Ids.Imdb}_{language.Id}",
                        () => _searchProxy.SearchForNewSeriesByImdbId(show.Ids.Imdb, language).FirstOrDefault(),
                        TimeSpan.FromHours(2));
                }

                if (show.Ids?.Tmdb is > 0)
                {
                    var tmdbId = show.Ids.Tmdb.Value;

                    return _seriesMetadataCache.Get(
                        $"tmdb_{tmdbId}_{language.Id}",
                        () => _searchProxy.SearchForNewSeriesByTmdbId(tmdbId, language).FirstOrDefault(),
                        TimeSpan.FromHours(2));
                }
            }
            catch (SeriesNotFoundException)
            {
                _logger.Debug("Recommended series '{0}' (TVDB: {1}) was not found on SkyHook.", show.Title, show.Ids?.Tvdb);
            }
            catch (OperationCanceledException)
            {
                // Request was cancelled by client
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to resolve metadata for recommended series '{0}' (TVDB: {1}).", show.Title, show.Ids?.Tvdb);
            }

            return null;
        }
    }
}
