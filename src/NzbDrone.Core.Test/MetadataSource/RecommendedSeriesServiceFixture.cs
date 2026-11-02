using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class RecommendedSeriesServiceFixture : CoreTest<RecommendedSeriesService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.AllSeriesTvdbIds())
                  .Returns(new Dictionary<int, int>());
        }

        private void GivenTraktResponse(string pathSuffix, string json)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.GetAsync(It.Is<HttpRequest>(r => r.Url.Path.EndsWith(pathSuffix)), It.IsAny<CancellationToken>()))
                  .Returns<HttpRequest, CancellationToken>((r, _) => Task.FromResult(new HttpResponse(r, new HttpHeader(), json, HttpStatusCode.OK)));
        }

        private void GivenLookup(int tvdbId, Series series)
        {
            Mocker.GetMock<ISearchForNewSeries>()
                  .Setup(p => p.LookupSeriesByTvdbIdAsync(tvdbId, Language.English, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(series);
        }

        [Test]
        public void should_return_recommended_trending_series()
        {
            GivenTraktResponse("/shows/trending", "[{\"watchers\":100,\"show\":{\"title\":\"Severance\",\"year\":2022,\"ids\":{\"trakt\":1,\"tvdb\":371980,\"imdb\":\"tt11280740\"}}}]");
            GivenLookup(371980, new Series { TvdbId = 371980, Title = "Severance" });

            var result = Subject.GetRecommendedSeries(RecommendationListType.Trending, Language.English);

            result.Should().ContainSingle();
            result[0].TvdbId.Should().Be(371980);
            result[0].Title.Should().Be("Severance");
        }

        [Test]
        public void should_return_recommended_popular_series()
        {
            GivenTraktResponse("/shows/popular", "[{\"title\":\"Breaking Bad\",\"year\":2008,\"ids\":{\"trakt\":2,\"tvdb\":81189,\"imdb\":\"tt0903747\"}}]");
            GivenLookup(81189, new Series { TvdbId = 81189, Title = "Breaking Bad" });

            var result = Subject.GetRecommendedSeries(RecommendationListType.Popular, Language.English);

            result.Should().ContainSingle();
            result[0].TvdbId.Should().Be(81189);
            result[0].Title.Should().Be("Breaking Bad");
        }

        [Test]
        public void should_use_existing_series_when_in_library()
        {
            GivenTraktResponse("/shows/trending", "[{\"watchers\":100,\"show\":{\"title\":\"Severance\",\"year\":2022,\"ids\":{\"trakt\":1,\"tvdb\":371980}}}]");

            var existingSeries = new Series { Id = 42, TvdbId = 371980, Title = "Severance" };

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.AllSeriesTvdbIds())
                  .Returns(new Dictionary<int, int> { { 42, 371980 } });

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Series> { existingSeries });

            var result = Subject.GetRecommendedSeries(RecommendationListType.Trending, Language.English);

            result.Should().ContainSingle();
            result[0].Should().BeSameAs(existingSeries);

            Mocker.GetMock<ISearchForNewSeries>()
                  .Verify(p => p.LookupSeriesByTvdbIdAsync(It.IsAny<int>(), It.IsAny<Language>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void should_skip_series_not_found_on_skyhook()
        {
            GivenTraktResponse("/shows/trending", "[{\"watchers\":100,\"show\":{\"title\":\"Missing Show\",\"year\":2026,\"ids\":{\"trakt\":1,\"tvdb\":999999}}}]");

            Mocker.GetMock<ISearchForNewSeries>()
                  .Setup(p => p.LookupSeriesByTvdbIdAsync(999999, Language.English, It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new SeriesNotFoundException(999999));

            var result = Subject.GetRecommendedSeries(RecommendationListType.New, Language.English);

            result.Should().BeEmpty();
        }

        [Test]
        public void should_return_empty_when_trakt_errors()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.GetAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                  .Returns<HttpRequest, CancellationToken>((r, _) => Task.FromResult(new HttpResponse(r, new HttpHeader(), "", HttpStatusCode.ServiceUnavailable)));

            var result = Subject.GetRecommendedSeries(RecommendationListType.Trending, Language.English);

            result.Should().BeEmpty();
        }
    }
}
