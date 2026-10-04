/**
 * 角色图：运行期热链百科（clocktower-wiki.gstonegames.com）图片。
 *
 * 依据 D-0007（美术资源：运行期热链百科图片；仓库永不出现官方图片文件）与 R-0006（热链可用性实测）。
 *
 * 文件名逐条取自 `references/wiki/<角色页>.wiki` 的首行 `[[File:…]]`（25 个非旅行者为 2026-10-01 快照，
 * 5 名旅行者为 2026-10-04 快照）；路径按 MediaWiki 的文件布局规则由文件名派生：
 * `/images/<md5(文件名)[0]>/<md5(文件名)[0:2]>/<文件名>`。
 * 30 条地址 2026-10-04 全量实测：200 + image/png（见 `artifacts/web/wiki-image-probe.log`）。
 *
 * 未知 slug 返回 null：不猜、不请求，由调用方退回文字呈现（同 `labels.ts` 的「未知取值原样回显」）。
 */
const WIKI_BASE_URL = 'https://clocktower-wiki.gstonegames.com'

/** 首版 30 人花名册（25 非旅行者 + 5 旅行者）的百科图片路径（相对 `/images/`，含 md5 目录）。 */
const CHARACTER_ART_PATHS: Readonly<Record<string, string>> = {
  clockmaker: '7/74/Clockmaker.png',
  dreamer: '2/29/Dreamer.png',
  'snake-charmer': 'a/ab/Snakecharmer.png',
  mathematician: '0/06/Mathematician.png',
  flowergirl: '5/5b/Flowergirl.png',
  'town-crier': 'b/b7/Towncrier.png',
  oracle: '1/17/Oracle.png',
  savant: '5/53/Savant.png',
  seamstress: 'a/ae/Seamstress.png',
  philosopher: 'e/e0/Philosopher.png',
  artist: 'a/a5/Artist.png',
  juggler: 'b/be/Juggler.png',
  sage: 'f/f8/Sage.png',
  mutant: '5/58/Mutant.png',
  sweetheart: 'c/c2/Sweetheart.png',
  barber: '3/3a/Barber.png',
  klutz: 'f/f6/Klutz.png',
  'evil-twin': '8/8e/Eviltwin.png',
  witch: 'e/e6/Witch.png',
  cerenovus: '4/4d/Cerenovus.png',
  'pit-hag': 'd/de/Pithag.png',
  'fang-gu': '9/97/Fanggu.png',
  vigormortis: 'f/fc/Vigormortis.png',
  'no-dashii': '1/16/Nodashii.png',
  vortox: '7/78/Vortox.png',
  // 旅行者（2026-10-04 快照的首行 File 名 → 同日 md5 派生 + 探活 200 + image/png）。
  deviant: 'd/d5/Deviant.png',
  'bone-collector': '4/48/Bonecollector.png',
  barista: '0/05/Barista.png',
  harlot: '8/89/Harlot.png',
  butcher: 'f/fb/Butcher.png',
}

/** 角色 slug → 百科图片地址；不在花名册里的 slug 返回 null（不发起请求）。 */
export function characterArtUrlOf(slug: string | null | undefined): string | null {
  if (slug === null || slug === undefined || slug === '') {
    return null
  }

  const artPath = CHARACTER_ART_PATHS[slug]
  return artPath === undefined ? null : `${WIKI_BASE_URL}/images/${artPath}`
}
