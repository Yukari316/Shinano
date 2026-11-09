/** 登录状态。 */
export type LoginState = 'Unknown' | 'LoggedOut' | 'LoggedIn' | 'Expired'

/** 扫码进度。 */
export type QrState = 'Waiting' | 'Scanned' | 'Succeeded' | 'Expired' | 'Failed'

/** B站账号。 */
export interface Account {
  uid: number
  name: string
}

/** 登录状态。 */
export interface Status {
  state: LoginState
  account: Account | null
  riskUntil: string | null
}

/** 二维码。 */
export interface QrView {
  key: string
  image: string
}

/** 扫码进度查询结果。 */
export interface QrStateView {
  state: QrState
  account: Account | null
}

/** 关注的 UP 与推送设置。 */
export interface Follow {
  uid: number
  name: string
  face: string
  lastDynamicAt: string | null
  pushDynamic: boolean
  pushLive: boolean
  groups: number[]
}

/** 可选的推送群。 */
export interface Group {
  id: number
  name: string | null
}
