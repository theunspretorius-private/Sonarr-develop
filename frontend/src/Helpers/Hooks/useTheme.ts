import { useLayoutEffect, useState } from 'react';

// The UI only ships a dark theme.
const useTheme = (): 'dark' => 'dark';

export default useTheme;

const readThemeVar = (color: string) =>
  getComputedStyle(document.documentElement)
    .getPropertyValue(`--${color}`)
    .trim();

export const useThemeColor = (color: string) => {
  const theme = useTheme();
  const [value, setValue] = useState(() => readThemeVar(color));

  useLayoutEffect(() => {
    setValue(readThemeVar(color));
  }, [color, theme]);

  return value;
};
