export function versionLabel(v) {
  return v.removeSex ? (v.muteLanguage ? 'Sex/nudity + language filtered' : 'Sex/nudity filtered')
    : v.muteLanguage ? 'Language filtered' : 'Original'
}
