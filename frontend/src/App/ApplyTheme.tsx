import { useLayoutEffect } from 'react';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';

function ApplyTheme() {
  const { enableColorImpairedMode, headerColor } = useUiSettingsValues();

  useLayoutEffect(() => {
    document.documentElement.style.removeProperty('--pageBackground');
  }, []);

  useLayoutEffect(() => {
    if (enableColorImpairedMode) {
      document.documentElement.dataset.colorImpaired = '';
    } else {
      delete document.documentElement.dataset.colorImpaired;
    }
  }, [enableColorImpairedMode]);

  useLayoutEffect(() => {
    if (headerColor === 'gradient') {
      document.documentElement.dataset.headerColor = headerColor;
    } else {
      delete document.documentElement.dataset.headerColor;
    }
  }, [headerColor]);

  return null;
}

export default ApplyTheme;
