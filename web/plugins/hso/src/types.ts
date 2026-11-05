/** 群设置（`/groups`）。 */
export interface Group {
  groupId: number
  /** Bot 已不在该群时为 null。 */
  name: string | null
  blocked: boolean
  allowNsfw: boolean
}

export type InfoState = 'Pending' | 'Found' | 'NotFound' | 'Review'
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

export type LookupMethod = 'Pixiv' | 'SauceNao'

/** 审核中对比的一张图片。 */
export interface ReviewImage {
  mediaType: string
  size: number
  width: number | null
  height: number | null
  sha256: string
}

/** 审核队列中的一张图片（`/reviews/{id}`）。 */
export interface Review {
  imageId: number
  fileName: string | null
  origin: ImageOrigin
  importedAt: string
  current: ReviewImage
  /** 原图下载失败时为 null，原因见 fetchError。 */
  candidate: ReviewImage | null
  fetchError: string | null
  method: LookupMethod
  similarity: number | null
  workSite: string | null
  workId: string | null
  workPage: number | null
  createdAt: string
  sourceName: string | null
  title: string | null
  author: string | null
  authorId: number | null
  pid: number | null
  page: number | null
  tags: string[]
  nsfw: boolean
}

/** 否决时手动补全的作品信息；全部留空时记为查无信息。 */
export interface ManualInfo {
  title: string | null
  author: string | null
  authorId: number | null
  pid: number | null
  page: number | null
  tags: string[]
  nsfw: boolean
}

export type ImportOutcome = 'Imported' | 'Duplicate' | 'Unsupported' | 'Failed'

/** 上传中一个文件的结果。 */
export interface ImportResult {
  fileName: string | null
  outcome: ImportOutcome
  id: number | null
}
