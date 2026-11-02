using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.SeriesStats;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Series;

[V5ApiController("series/lookup")]
public class SeriesLookupController : Controller
{
    private readonly ISearchForNewSeries _searchProxy;
    private readonly IProvideRecommendedSeries _recommendedSeriesProxy;
    private readonly IBuildFileNames _fileNameBuilder;
    private readonly IMapCoversToLocal _coverMapper;
    private readonly IImportListExclusionService _importListExclusionService;
    private readonly IConfigService _configService;

    public SeriesLookupController(
        ISearchForNewSeries searchProxy,
        IProvideRecommendedSeries recommendedSeriesProxy,
        IBuildFileNames fileNameBuilder,
        IMapCoversToLocal coverMapper,
        IImportListExclusionService importListExclusionService,
        IConfigService configService)
    {
        _searchProxy = searchProxy;
        _recommendedSeriesProxy = recommendedSeriesProxy;
        _fileNameBuilder = fileNameBuilder;
        _coverMapper = coverMapper;
        _importListExclusionService = importListExclusionService;
        _configService = configService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<IEnumerable<SeriesResource>> Search([FromQuery] string term, [FromQuery] int? language = null)
    {
        var languageId = language ?? _configService.PreferredMetadataLanguage;
        var searchLanguage = Language.All.FirstOrDefault(l => l.Id == languageId);

        if (searchLanguage == null || IsoLanguages.Get(searchLanguage) == null)
        {
            throw new BadRequestException($"Invalid language: {languageId}");
        }

        var tvDbResults = _searchProxy.SearchForNewSeries(term, searchLanguage);
        return TypedResults.Ok(MapToResource(tvDbResults));
    }

    [HttpGet("recommended")]
    [Produces("application/json")]
    public async Task<Ok<IEnumerable<SeriesResource>>> GetRecommended(
        [FromQuery] RecommendationListType listType = RecommendationListType.Trending,
        [FromQuery] int? language = null,
        CancellationToken cancellationToken = default)
    {
        var languageId = language ?? _configService.PreferredMetadataLanguage;
        var searchLanguage = Language.All.FirstOrDefault(l => l.Id == languageId);

        if (searchLanguage == null || IsoLanguages.Get(searchLanguage) == null)
        {
            throw new BadRequestException($"Invalid language: {languageId}");
        }

        var results = await _recommendedSeriesProxy.GetRecommendedSeriesAsync(listType, searchLanguage, cancellationToken);
        return TypedResults.Ok(MapToResource(results));
    }

    private IEnumerable<SeriesResource> MapToResource(IEnumerable<NzbDrone.Core.Tv.Series> series)
    {
        var excludedTvdbIds = _importListExclusionService.All().Select(e => e.TvdbId).ToHashSet();

        foreach (var currentSeries in series)
        {
            var resource = currentSeries.ToResource();

            _coverMapper.ConvertToLocalUrls(resource.Id, resource.Images, resource.Added);

            var poster = currentSeries.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);

            if (poster != null)
            {
                resource.RemotePoster = poster.RemoteUrl;
            }

            resource.Folder = _fileNameBuilder.GetSeriesFolder(currentSeries);
            resource.Folders = [];
            resource.Statistics = new SeriesStatistics().ToResource(resource.Seasons);
            resource.IsExcluded = excludedTvdbIds.Contains(currentSeries.TvdbId);

            var folderByTitle = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!string.IsNullOrEmpty(currentSeries.Title))
            {
                folderByTitle[currentSeries.Title] = resource.Folder;
            }

            foreach (var translation in currentSeries.Translations)
            {
                string folder;

                if (string.IsNullOrWhiteSpace(translation.Title))
                {
                    folder = resource.Folder;
                }
                else if (!folderByTitle.TryGetValue(translation.Title, out folder!))
                {
                    folder = _fileNameBuilder.GetSeriesFolder(currentSeries, translation.Title);
                    folderByTitle[translation.Title] = folder;
                }

                resource.Folders.Add(new SeriesFolderResource
                {
                    Language = translation.Language,
                    Folder = folder
                });
            }

            yield return resource;
        }
    }
}
