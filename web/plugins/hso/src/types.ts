/** 群设置（`/groups`）。 */
export interface Group {
  groupId: number
  /** Bot 已不在该群时为 null。 */
  name: string | null
  blocked: boolean
  allowNsfw: boolean
}

export type InfoState = 'Pending' | 'Found' | 'NotFound'
export type ImageOrigin = 'Upload' | 'Zip' | 'Pixiv' | 'Tool'

/** 图库中的一张图片（`/images`）。 */
export interface GalleryImage {
  id: number
  fileName: string | null
  mediaType: string
  size: number
  pid: number | null
  page: number | null
  title: string | null
  author: string | null
  sourceName: string | null
  nsfw: boolean
  nsfwManual: boolean
  blocked: boolean
  infoState: InfoState
  origin: ImageOrigin
  importedAt: string
}

export interface ImagePage {
  total: number
  items: GalleryImage[]
}

export type ImportOutcome = 'Imported' | 'Duplicate' | 'Unsupported' | 'Failed'

/** 上传中一个文件的结果。 */
export interface ImportResult {
  fileName: string | null
  outcome: ImportOutcome
  id: number | null
}
