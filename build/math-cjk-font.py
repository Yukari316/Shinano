# /// script
# requires-python = ">=3.10"
# dependencies = ["fonttools==4.60.1"]
# ///
# 生成宿主公式渲染的中文后备字体：src/Shinano.Host/Resource/Fonts/NotoSansSC-Regular-GB2312.ttf。
# CSharpMath 自带字体没有中文字形，它使用的 Typography 又无法解析 Noto Sans SC 的 CFF 字形（抛 NotSupportedException），
# 因此从宿主现有的 NotoSansSC-Regular.otf 截取 GB2312 汉字、CJK 标点与全角字符，把三次曲线转为二次曲线的 TrueType 轮廓。
# 子集不含 ASCII：CSharpMath 先查后备字体，含 ASCII 会让公式里的数字、字母与运算符也改用 Noto Sans SC。
# 不重算时间戳，同一源字体重复生成得到相同的文件。需要 uv。
# 用法：uv run build/math-cjk-font.py
from pathlib import Path

from fontTools import subset
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont, newTable

FONTS = Path(__file__).resolve().parent.parent / "src" / "Shinano.Host" / "Resource" / "Fonts"
SOURCE = FONTS / "NotoSansSC-Regular.otf"
TARGET = FONTS / "NotoSansSC-Regular-GB2312.ttf"

# 二次曲线逼近三次曲线的最大误差，单位为字体单位（1000 UPM），远小于渲染像素。
MAX_CURVE_ERROR = 1.0

CJK_SYMBOLS = range(0x3000, 0x3040)
CJK_IDEOGRAPHS = range(0x4E00, 0xA000)
FULLWIDTH_FORMS = range(0xFF00, 0xFFF0)


def is_gb2312(code):
    try:
        chr(code).encode("gb2312")
        return True
    except UnicodeEncodeError:
        return False


def unicodes():
    codes = set(CJK_SYMBOLS) | set(FULLWIDTH_FORMS)
    codes |= {code for code in CJK_IDEOGRAPHS if is_gb2312(code)}
    return codes


def subset_font(font):
    options = subset.Options()
    options.layout_features = []
    options.name_IDs = ["*"]
    options.notdef_outline = True
    options.hinting = False
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=unicodes())
    subsetter.subset(font)


def cff_to_glyf(font):
    order = font.getGlyphOrder()
    glyph_set = font.getGlyphSet()
    glyphs = {}
    for name in order:
        pen = TTGlyphPen(glyph_set)
        glyph_set[name].draw(Cu2QuPen(pen, MAX_CURVE_ERROR, reverse_direction=True))
        glyphs[name] = pen.glyph()

    font["loca"] = newTable("loca")
    font["glyf"] = glyf = newTable("glyf")
    glyf.glyphOrder = order
    glyf.glyphs = glyphs
    del font["CFF "]
    if "VORG" in font:
        del font["VORG"]
    glyf.compile(font)

    hmtx = font["hmtx"]
    for name, glyph in glyf.glyphs.items():
        if hasattr(glyph, "xMin"):
            hmtx[name] = (hmtx[name][0], glyph.xMin)

    font["maxp"] = maxp = newTable("maxp")
    maxp.tableVersion = 0x00010000
    maxp.maxZones = 1
    maxp.maxTwilightPoints = 0
    maxp.maxStorage = 0
    maxp.maxFunctionDefs = 0
    maxp.maxInstructionDefs = 0
    maxp.maxStackElements = 0
    maxp.maxSizeOfInstructions = 0
    maxp.maxComponentElements = 0
    maxp.compile(font)

    post = font["post"]
    post.formatType = 2.0
    post.extraNames = []
    post.mapping = {}
    post.glyphOrder = order
    font.sfntVersion = "\000\001\000\000"


def main():
    font = TTFont(SOURCE, recalcTimestamp=False)
    subset_font(font)
    cff_to_glyf(font)
    font.save(TARGET)
    print(f"{TARGET.name}: {len(font.getGlyphOrder())} glyphs, {TARGET.stat().st_size} bytes")


main()
