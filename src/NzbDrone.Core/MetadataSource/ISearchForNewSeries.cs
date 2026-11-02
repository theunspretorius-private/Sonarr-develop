using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISearchForNewSeries
    {
        List<Series> SearchForNewSeries(string title, Language language);
        List<Series> SearchForNewSeriesByImdbId(string imdbId, Language language);
        List<Series> SearchForNewSeriesByAniListId(int aniListId, Language language);
        List<Series> SearchForNewSeriesByTmdbId(int tmdbId, Language language);
        List<Series> SearchForNewSeriesByMyAnimeListId(int malId, Language language);
        Task<Series> LookupSeriesByTvdbIdAsync(int tvdbSeriesId, Language language, CancellationToken cancellationToken = default);
    }
}
