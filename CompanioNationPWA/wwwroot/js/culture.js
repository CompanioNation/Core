window.blazorCulture = {
    get: () => localStorage.getItem('culture'),
    set: (value) => localStorage.setItem('culture', value),
    getBrowserLanguages: () => navigator.languages || [navigator.language],
    setDocLang: (lang) => document.documentElement.lang = lang
};
