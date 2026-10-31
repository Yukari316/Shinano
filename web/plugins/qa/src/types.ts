// 有人问管理 API 的数据类型，与插件后端的 JSON 一致。

export type MatchType = 'Full' | 'Regex'
export type PartKind = 'Text' | 'Face' | 'Mention' | 'Image' | 'Audio' | 'Video' | 'File'

export interface AnswerPart {
  kind: PartKind
  text?: string
  faceId?: string
  largeFace?: boolean
  userId?: number
  mediaId?: number
  fileName?: string
}

export interface Entry {
  id: number
  groupId: number
  matchType: MatchType
  question: string
  answer: AnswerPart[]
  createdBy: number
  createdAt: string
  version: number
}

export interface Group {
  groupId: number
  name: string
}

export interface ForbiddenQuestion {
  id: number
  question: string
}

/** 新增词条时回答的一段：文字、@ 某个 QQ 号或一个待上传的文件。 */
export type DraftPart =
  | { key: number; type: 'Text'; text: string }
  | { key: number; type: 'Mention'; userId: string }
  | { key: number; type: 'Upload'; file: File | null }
