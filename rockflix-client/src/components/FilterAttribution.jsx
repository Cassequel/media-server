export default function FilterAttribution({ notice = '', muteLanguage = false }) {
  const community = notice.includes('CleanStream / VideoSkip')
  return <span className="block mt-2 text-xs">
    {community && <><a className="underline" href="https://github.com/ameen-roayan/stremio-cleanstream" target="_blank" rel="noreferrer">CleanStream</a>
      {' / '}<a className="underline" href="https://videoskip.org/exchange" target="_blank" rel="noreferrer">VideoSkip community timestamps</a>{' '}</>}
    {(!community || muteLanguage) && <a className="underline" href="https://www.doesthedogdie.com" target="_blank" rel="noreferrer">Powered by DoesTheDogDie.com</a>}
  </span>
}
