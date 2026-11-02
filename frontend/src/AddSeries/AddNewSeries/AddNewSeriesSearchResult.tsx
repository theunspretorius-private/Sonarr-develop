import classNames from 'classnames';
import React, { useCallback, useState } from 'react';
import AddSeries from 'AddSeries/AddSeries';
import { useAddSeriesOptions } from 'AddSeries/addSeriesOptionsStore';
import { useAppDimension } from 'App/appStore';
import HeartRating from 'Components/HeartRating';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import MetadataAttribution from 'Components/MetadataAttribution';
import { icons, kinds, sizes } from 'Helpers/Props';
import { DEFAULT_LANGUAGE } from 'Language/Language';
import { useLanguageById } from 'Language/useLanguages';
import { Statistics } from 'Series/Series';
import SeriesGenres from 'Series/SeriesGenres';
import SeriesPoster from 'Series/SeriesPoster';
import useExistingSeries from 'Series/useExistingSeries';
import { useMetadataSourceSettingsValues } from 'Settings/MetadataSource/useMetadataSourceSettings';
import translate from 'Utilities/String/translate';
import AddNewSeriesModal from './AddNewSeriesModal';
import { useAddSeries } from './useAddSeries';
import styles from './AddNewSeriesSearchResult.module.css';

function getStatusChip(status: string) {
  switch (status) {
    case 'continuing':
      return { dot: styles.chipDotAiring, label: translate('Continuing') };
    case 'upcoming':
      return { dot: styles.chipDotUpcoming, label: translate('Upcoming') };
    case 'ended':
      return { dot: styles.chipDotEnded, label: translate('Ended') };
    case 'deleted':
      return { dot: styles.chipDotFinished, label: translate('Deleted') };
    default:
      return null;
  }
}

interface AddNewSeriesSearchResultProps {
  series: AddSeries;
}

function AddNewSeriesSearchResult({ series }: AddNewSeriesSearchResultProps) {
  const {
    tvdbId,
    titleSlug,
    title: lookupTitle,
    originalTitle,
    year,
    network,
    originalLanguage,
    genres = [],
    status,
    statistics = {} as Statistics,
    ratings,
    overview: lookupOverview,
    seriesType,
    seasonTypes = [],
    translations,
    images,
    isExcluded,
  } = series;

  const { preferredMetadataLanguage } = useMetadataSourceSettingsValues();
  const language = useLanguageById(preferredMetadataLanguage);
  const isExistingSeries = useExistingSeries(tvdbId);
  const isSmallScreen = useAppDimension('isSmallScreen');
  const [isNewAddSeriesModalOpen, setIsNewAddSeriesModalOpen] = useState(false);
  const options = useAddSeriesOptions();
  const { isAdding, addSeries } = useAddSeries();

  const seasonCount = statistics.seasonCount;
  const translation = translations.find((t) => t.language.id === language?.id);
  const title = translation?.title ?? originalTitle ?? lookupTitle;
  const overview = translation?.overview ?? lookupOverview;

  const handlePress = useCallback(() => {
    setIsNewAddSeriesModalOpen(true);
  }, []);

  const handleQuickAddPress = useCallback(() => {
    if (!options.rootFolderPath || !options.qualityProfileId) {
      setIsNewAddSeriesModalOpen(true);
      return;
    }

    const effectiveSeriesType =
      seriesType === 'standard' ? options.seriesType : seriesType;
    const effectiveSeasonType =
      seasonTypes.find((s) => s.type === 'official')?.type ??
      seasonTypes[0]?.type ??
      'official';

    addSeries({
      ...series,
      rootFolderPath: options.rootFolderPath,
      addOptions: {
        monitor: options.monitor,
        searchForMissingEpisodes: options.searchForMissingEpisodes,
        searchForCutoffUnmetEpisodes: options.searchForCutoffUnmetEpisodes,
      },
      qualityProfileId: options.qualityProfileId,
      seriesType: effectiveSeriesType,
      seasonType: effectiveSeasonType,
      language: language ?? DEFAULT_LANGUAGE,
      seasonFolder: options.seasonFolder,
      tags: options.tags,
    });
  }, [series, seriesType, seasonTypes, options, language, addSeries]);

  const handleAddSeriesModalClose = useCallback(() => {
    setIsNewAddSeriesModalOpen(false);
  }, []);

  const handleTvdbLinkPress = useCallback((event: React.SyntheticEvent) => {
    event.stopPropagation();
  }, []);

  const linkProps = isExistingSeries
    ? { to: `/series/${titleSlug}` }
    : { onPress: handlePress };
  let seasons = translate('OneSeason');

  if (seasonCount > 1) {
    seasons = translate('CountSeasons', { count: seasonCount });
  }

  const statusChip = getStatusChip(status);

  return (
    <div
      className={classNames(
        styles.searchResult,
        isExcluded && styles.searchResultExcluded
      )}
    >
      <Link
        className={styles.underlay}
        aria-label={
          isExistingSeries ? title : translate('AddSeriesWithTitle', { title })
        }
        {...linkProps}
      />

      <div className={styles.overlay}>
        {isSmallScreen ? null : (
          <SeriesPoster
            className={styles.poster}
            images={images}
            size={250}
            overflow={true}
            lazy={false}
            title={title}
          />
        )}

        <div className={styles.content}>
          <div className={styles.titleRow}>
            <div className={styles.titleContainer}>
              <div className={styles.title}>
                {title}

                {!title.includes(String(year)) && year ? (
                  <span className={styles.year}>({year})</span>
                ) : null}
              </div>
            </div>

            <div className={styles.icons}>
              {isExistingSeries ? (
                <Icon
                  className={styles.alreadyExistsIcon}
                  name={icons.CHECK_CIRCLE}
                  size={24}
                  title={translate('AlreadyInYourLibrary')}
                />
              ) : (
                <div className={styles.actionButtons}>
                  <SpinnerButton
                    className={styles.quickAddButton}
                    kind={kinds.PRIMARY}
                    size={sizes.SMALL}
                    isSpinning={isAdding}
                    title={translate('QuickAddSeriesTitle', { title })}
                    onPress={handleQuickAddPress}
                  >
                    <Icon
                      className={styles.quickAddIcon}
                      name={icons.ADD}
                      size={14}
                    />
                    {translate('QuickAdd')}
                  </SpinnerButton>

                  <Button
                    className={styles.configureAddButton}
                    kind={kinds.DEFAULT}
                    size={sizes.SMALL}
                    title={translate('AddSeriesWithTitle', { title })}
                    onPress={handlePress}
                  >
                    <Icon name={icons.SETTINGS} size={14} />
                  </Button>
                </div>
              )}

              {isExcluded ? (
                <Icon
                  className={styles.excludedIcon}
                  name={icons.DANGER}
                  size={24}
                  title={translate('SeriesInImportListExclusions')}
                />
              ) : null}

              <Link
                className={styles.tvdbLink}
                to={`https://www.thetvdb.com/?tab=series&id=${tvdbId}`}
                aria-label={translate('ViewSeriesOnTvdb', { title })}
                onPress={handleTvdbLinkPress}
              >
                <Icon name={icons.EXTERNAL_LINK} size={16} aria-hidden={true} />
              </Link>
            </div>
          </div>

          <div className={styles.chips}>
            {ratings.votes > 0 ? (
              <span className={styles.chip}>
                <HeartRating
                  rating={ratings.value}
                  votes={ratings.votes}
                  iconSize={11}
                />
              </span>
            ) : null}

            {statusChip ? (
              <span className={styles.chip}>
                <span className={classNames(styles.chipDot, statusChip.dot)} />
                {statusChip.label}
              </span>
            ) : null}

            {network ? <span className={styles.chip}>{network}</span> : null}

            {originalLanguage?.name ? (
              <span className={styles.chip}>{originalLanguage.name}</span>
            ) : null}

            {genres.length > 0 ? (
              <span className={styles.chip}>
                <SeriesGenres className={styles.genres} genres={genres} />
              </span>
            ) : null}

            {seasonCount ? (
              <span className={styles.chip}>{seasons}</span>
            ) : null}
          </div>

          <div className={styles.overview}>{overview}</div>

          <MetadataAttribution />
        </div>
      </div>

      <AddNewSeriesModal
        isOpen={isNewAddSeriesModalOpen && !isExistingSeries}
        series={series}
        initialSeriesType={seriesType}
        onModalClose={handleAddSeriesModalClose}
      />
    </div>
  );
}

export default AddNewSeriesSearchResult;
