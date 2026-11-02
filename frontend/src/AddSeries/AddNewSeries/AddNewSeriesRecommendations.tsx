import classNames from 'classnames';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  AddSeriesOptions,
  setAddSeriesOption,
  useAddSeriesOptions,
} from 'AddSeries/addSeriesOptionsStore';
import CheckInput from 'Components/Form/CheckInput';
import MonitorEpisodesSelectInput from 'Components/Form/Select/MonitorEpisodesSelectInput';
import QualityProfileSelectInput from 'Components/Form/Select/QualityProfileSelectInput';
import RootFolderSelectInput from 'Components/Form/Select/RootFolderSelectInput';
import SeriesTypeSelectInput from 'Components/Form/Select/SeriesTypeSelectInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { icons, kinds, sizes } from 'Helpers/Props';
import Language from 'Language/Language';
import useRootFolders from 'RootFolder/useRootFolders';
import useSeries, { useHasSeries } from 'Series/useSeries';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import AddNewSeriesSearchResult from './AddNewSeriesSearchResult';
import { RecommendationListType, useRecommendedSeries } from './useAddSeries';
import styles from './AddNewSeriesRecommendations.module.css';

const HIDE_HINT = { includeHint: false };

interface CategoryTab {
  key: RecommendationListType;
  labelKey: string;
}

const CATEGORY_TABS: CategoryTab[] = [
  { key: 'trending', labelKey: 'Trending' },
  { key: 'popular', labelKey: 'Popular' },
  { key: 'new', labelKey: 'NewAndRecent' },
  { key: 'anticipated', labelKey: 'Anticipated' },
  { key: 'topWatched', labelKey: 'TopWatched' },
];

interface RecommendationTabButtonProps {
  tab: CategoryTab;
  isActive: boolean;
  onSelect: (listType: RecommendationListType) => void;
}

function RecommendationTabButton({
  tab,
  isActive,
  onSelect,
}: RecommendationTabButtonProps) {
  const handlePress = useCallback(() => {
    onSelect(tab.key);
  }, [tab.key, onSelect]);

  return (
    <Button
      className={classNames(
        styles.tabButton,
        isActive && styles.tabButtonActive
      )}
      role="tab"
      aria-selected={isActive}
      onPress={handlePress}
    >
      {translate(tab.labelKey)}
    </Button>
  );
}

interface DefaultRootFolderInitializerProps {
  rootFolderPath: string;
}

// Ensures quick add always targets an existing root folder. A remembered path
// that was since removed (or renamed) would otherwise be used to build the new
// series path, leaving imports to move files into a folder Sonarr doesn't manage.
function DefaultRootFolderInitializer({
  rootFolderPath,
}: DefaultRootFolderInitializerProps) {
  const { data: rootFolders, isFetched } = useRootFolders();

  useEffect(() => {
    if (!isFetched) {
      return;
    }

    const isKnown = rootFolders.some((r) => r.path === rootFolderPath);

    if (isKnown) {
      return;
    }

    setAddSeriesOption('rootFolderPath', rootFolders[0]?.path ?? '');
  }, [isFetched, rootFolders, rootFolderPath]);

  return null;
}

interface AddNewSeriesRecommendationsProps {
  language: Language;
}

function AddNewSeriesRecommendations({
  language,
}: AddNewSeriesRecommendationsProps) {
  const hasSeries = useHasSeries();
  const { data: existingSeriesList = [] } = useSeries();
  const qualityProfiles = useQualityProfilesData();
  const [listType, setListType] = useState<RecommendationListType>('trending');
  const [showExistingSeries, setShowExistingSeries] = useState(false);
  const [isDefaultsOpen, setIsDefaultsOpen] = useState(false);

  const {
    rootFolderPath,
    qualityProfileId,
    monitor,
    seriesType,
    searchForMissingEpisodes,
  } = useAddSeriesOptions();

  useEffect(() => {
    if (
      qualityProfiles.length > 0 &&
      !qualityProfiles.some((p) => p.id === qualityProfileId)
    ) {
      setAddSeriesOption('qualityProfileId', qualityProfiles[0].id);
    }
  }, [qualityProfileId, qualityProfiles]);

  const { isFetching, error, data } = useRecommendedSeries(listType, language);

  const existingTvdbIds = useMemo(() => {
    return new Set(existingSeriesList.map((s) => s.tvdbId));
  }, [existingSeriesList]);

  const existingInListCount = useMemo(() => {
    return data.filter((item) => existingTvdbIds.has(item.tvdbId)).length;
  }, [data, existingTvdbIds]);

  const visibleSeries = useMemo(() => {
    if (showExistingSeries) {
      return data;
    }

    return data.filter((item) => !existingTvdbIds.has(item.tvdbId));
  }, [data, existingTvdbIds, showExistingSeries]);

  const handleOptionChange = useCallback(
    ({
      name,
      value,
    }: InputChanged<string | number | boolean | number[] | Language>) => {
      setAddSeriesOption(name as keyof AddSeriesOptions, value);
    },
    []
  );

  const handleToggleShowExisting = useCallback(() => {
    setShowExistingSeries((prev) => !prev);
  }, []);

  const handleToggleDefaults = useCallback(() => {
    setIsDefaultsOpen((prev) => !prev);
  }, []);

  let showExistingButtonText = translate('ShowInLibrarySeries');

  if (showExistingSeries) {
    showExistingButtonText = translate('HideInLibrarySeries');
  } else if (existingInListCount > 0) {
    showExistingButtonText = translate('ShowInLibrarySeriesCount', {
      count: existingInListCount,
    });
  }

  return (
    <div className={styles.container}>
      {isDefaultsOpen ? null : (
        <DefaultRootFolderInitializer rootFolderPath={rootFolderPath} />
      )}

      {hasSeries ? null : (
        <div className={styles.importCallout}>
          <span className={styles.importCalloutText}>
            {translate('NoSeriesHaveBeenAdded')}
          </span>
          <Button to="/add/import" kind={kinds.PRIMARY} size={sizes.SMALL}>
            {translate('ImportExistingSeries')}
          </Button>
        </div>
      )}

      <div className={styles.headerRow}>
        <div className={styles.titleGroup}>
          <div className={styles.sectionTitle}>
            {translate('RecommendedSeries')}
          </div>
          <div className={styles.sectionSubtitle}>
            {translate('RecommendedSeriesHelpText')}
          </div>
        </div>
      </div>

      <div className={styles.controlsRow}>
        <div className={styles.tabs} role="tablist">
          {CATEGORY_TABS.map((tab) => (
            <RecommendationTabButton
              key={tab.key}
              tab={tab}
              isActive={listType === tab.key}
              onSelect={setListType}
            />
          ))}
        </div>

        <div className={styles.filterActions}>
          <Button
            kind={kinds.DEFAULT}
            size={sizes.SMALL}
            onPress={handleToggleDefaults}
          >
            <Icon name={icons.SETTINGS} size={14} />{' '}
            {translate('QuickAddDefaults')}
          </Button>

          <Button
            kind={kinds.DEFAULT}
            size={sizes.SMALL}
            onPress={handleToggleShowExisting}
          >
            <Icon name={icons.VIEW} size={14} /> {showExistingButtonText}
          </Button>
        </div>
      </div>

      {isDefaultsOpen ? (
        <div className={styles.defaultsBar}>
          <div className={styles.defaultsHeader}>
            <div className={styles.defaultsTitle}>
              {translate('QuickAddDefaults')}
            </div>

            <div className={styles.searchCheckContainer}>
              <CheckInput
                name="searchForMissingEpisodes"
                value={searchForMissingEpisodes}
                helpText={translate('SearchForMissingEpisodesOnAdd')}
                onChange={handleOptionChange}
              />
            </div>
          </div>

          <div className={styles.defaultsGrid}>
            <div className={styles.setting}>
              <div className={styles.settingLabel}>
                {translate('RootFolder')}
              </div>
              <RootFolderSelectInput
                name="rootFolderPath"
                value={rootFolderPath}
                includeMissingValue={false}
                onChange={handleOptionChange}
              />
            </div>

            <div className={styles.setting}>
              <div className={styles.settingLabel}>
                {translate('QualityProfile')}
              </div>
              <QualityProfileSelectInput
                name="qualityProfileId"
                value={qualityProfileId}
                onChange={handleOptionChange}
              />
            </div>

            <div className={styles.setting}>
              <div className={styles.settingLabel}>{translate('Monitor')}</div>
              <MonitorEpisodesSelectInput
                name="monitor"
                value={monitor}
                onChange={handleOptionChange}
              />
            </div>

            <div className={styles.setting}>
              <div className={styles.settingLabel}>
                {translate('SeriesType')}
              </div>
              <SeriesTypeSelectInput
                name="seriesType"
                value={seriesType}
                selectedValueOptions={HIDE_HINT}
                onChange={handleOptionChange}
              />
            </div>
          </div>
        </div>
      ) : null}

      {isFetching ? <LoadingIndicator /> : null}

      {!isFetching && !!error ? (
        <div className={styles.emptyState}>
          <div className={styles.emptyTitle}>
            {translate('RecommendedSeriesError')}
          </div>
          <p className={styles.error}>{getErrorMessage(error)}</p>
        </div>
      ) : null}

      {!isFetching && !error && visibleSeries.length > 0 ? (
        <div>
          <div className={styles.resultsLabel}>
            {visibleSeries.length === 1
              ? translate('CountResult', { count: visibleSeries.length })
              : translate('CountResults', { count: visibleSeries.length })}
          </div>

          {visibleSeries.map((item) => (
            <AddNewSeriesSearchResult
              key={item.tvdbId}
              series={item}
              isExistingSeries={existingTvdbIds.has(item.tvdbId)}
              language={language}
            />
          ))}
        </div>
      ) : null}

      {!isFetching && !error && visibleSeries.length === 0 ? (
        <div className={styles.emptyState}>
          <div className={styles.emptyTitle}>
            {data.length > 0 && !showExistingSeries
              ? translate('AllRecommendedSeriesInLibrary')
              : translate('NoRecommendedSeriesFound')}
          </div>
          {data.length > 0 && !showExistingSeries ? (
            <>
              <div className={styles.emptyBody}>
                {translate('AllRecommendedSeriesInLibraryHelpText')}
              </div>
              <Button kind={kinds.DEFAULT} onPress={handleToggleShowExisting}>
                {translate('ShowInLibrarySeriesCount', {
                  count: existingInListCount,
                })}
              </Button>
            </>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

export default AddNewSeriesRecommendations;
