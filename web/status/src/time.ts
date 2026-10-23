/** 按部署时区格式化时间；时区 ID 浏览器不认识（如 Windows 时区名）时使用浏览器本地时区。 */
export function createFormatter(timeZone: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  try {
    return new Intl.DateTimeFormat('zh-CN', { ...options, timeZone, hour12: false })
  } catch {
    return new Intl.DateTimeFormat('zh-CN', { ...options, hour12: false })
  }
}
