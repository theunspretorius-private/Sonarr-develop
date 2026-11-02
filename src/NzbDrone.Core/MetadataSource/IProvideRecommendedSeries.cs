using System.Collections.Generic;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public enum RecommendationListType
    {
        Trending = 0,
        Popular = 1,
        New = 2,
        Anticipated = 3,
        TopWatched = 4
    }

    public interface IProvideRecommendedSeries
    {
        List<Series> GetRecommendedSeries(RecommendationListType listType, Language language);
    }
}

