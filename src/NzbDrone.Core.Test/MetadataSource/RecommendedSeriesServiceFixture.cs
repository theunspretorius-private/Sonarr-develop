using System;
using System.Collections.Generic;
using System.Net;
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
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series>());
        }

        [Test]
        public void should_return_recommended_trending_series()
        {
            var traktJson = "[{\"watchers\":100,\"show\":{\"title\":\"Severance\",\"year\":2022,\"ids\":{\"trakt\":1,\"tvdb\":371980,\"imdb\":\"tt11280740\"}}}]";

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.Path.EndsWith("/shows/trending"))))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), traktJson, HttpStatusCode.OK));

            var expectedSeries = new Series { TvdbId = 371980, Title = "Severance" };

            Mocker.GetMock<IProvideSeriesInfo>()
                  .Setup(p => p.GetSeriesInfo(371980, Language.English, SeasonType.Official))
                  .Returns(Tuple.Create(expectedSeries, new List<Episode>()));

            var result = Subject.GetRecommendedSeries(RecommendationListType.Trending, Language.English);

            result.Should().ContainSingle();
            result[0].TvdbId.Should().Be(371980);
            result[0].Title.Should().Be("Severance");
        }

        [Test]
        public void should_return_recommended_popular_series()
        {
            var traktJson = "[{\"title\":\"Breaking Bad\",\"year\":2008,\"ids\":{\"trakt\":2,\"tvdb\":81189,\"imdb\":\"tt0903747\"}}]";

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.Path.EndsWith("/shows/popular"))))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), traktJson, HttpStatusCode.OK));

            var expectedSeries = new Series { TvdbId = 81189, Title = "Breaking Bad" };

            Mocker.GetMock<IProvideSeriesInfo>()
                  .Setup(p => p.GetSeriesInfo(81189, Language.English, SeasonType.Official))
                  .Returns(Tuple.Create(expectedSeries, new List<Episode>()));

            var result = Subject.GetRecommendedSeries(RecommendationListType.Popular, Language.English);

            result.Should().ContainSingle();
            result[0].TvdbId.Should().Be(81189);
            result[0].Title.Should().Be("Breaking Bad");
        }

        [Test]
        public void should_use_existing_series_when_in_library()
        {
            var traktJson = "[{\"watchers\":100,\"show\":{\"title\":\"Severance\",\"year\":2022,\"ids\":{\"trakt\":1,\"tvdb\":371980}}}]";

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), traktJson, HttpStatusCode.OK));

            var existingSeries = new Series { Id = 42, TvdbId = 371980, Title = "Severance" };

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series> { existingSeries });

            var result = Subject.GetRecommendedSeries(RecommendationListType.Trending, Language.English);

            result.Should().ContainSingle();
            result[0].Should().BeSameAs(existingSeries);

            Mocker.GetMock<IProvideSeriesInfo>()
                  .Verify(p => p.GetSeriesInfo(It.IsAny<int>(), It.IsAny<Language>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_skip_series_not_found_on_skyhook()
        {
            var traktJson = "[{\"watchers\":100,\"show\":{\"title\":\"Missing Show\",\"year\":2026,\"ids\":{\"trakt\":1,\"tvdb\":999999}}}]";

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), traktJson, HttpStatusCode.OK));

            Mocker.GetMock<IProvideSeriesInfo>()
                  .Setup(p => p.GetSeriesInfo(999999, Language.English, SeasonType.Official))
                  .Throws(new SeriesNotFoundException(999999));

            var result = Subject.GetRecommendedSeries(RecommendationListType.New, Language.English);

            result.Should().BeEmpty();
        }
    }
}

