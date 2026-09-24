#ifndef EXERUSSUS_OUTLINE_UI_FILTER_INCLUDED
#define EXERUSSUS_OUTLINE_UI_FILTER_INCLUDED

// Фильтр подсветки UI Toolkit. Поддерево элемента растеризовано в текстуру (_MainTex); проходы фильтра идут
// цепочкой — каждый читает выход предыдущего. Внутри силуэта (альфа > 0) хранится исходный цвет, снаружи
// (альфа = 0) RGB свободны — туда пишется смещение до ближайшего сида (JFA): 12 + 12 бит со сдвигом 2048.
// Координаты p — в пикселях прямоугольника фильтра; ось y идёт вниз по экрану.

#include "UnityCG.cginc"
#include "UnityUIEFilter.cginc"

#define OL_TWO_PI 6.28318530718
#define OL_EMPTY_BITS 4095.0
#define OL_OCCUPIED 0.002

Texture2D _MainTex;
SamplerState sampler_LinearClamp;
SamplerState sampler_LinearRepeat;

// заполняются из C# на каждый проход (OutlineUiFilter.ApplySettings)
float4 _OlPx;        // x пикселей на пункт, y время, z источник в гамме, w выход в гамме
float4 _OlStep;      // x шаг JFA, px
float4 _OlOuter;     // цвет свечения (a — сила)
float4 _OlInner;
float4 _OlFill;
float4 _OlWidths;    // x внешний px, y внутренний px, z fade, w additive
float4 _OlPulse;     // x скорость, y альфа, z ширина
float4 _OlNoise;     // x масштаб px, y сила, z скорость
float4 _OlPattern;   // x тип, y период, z угол (рад), w скорость
float4 _OlPattern2;  // x заполнение, y сила, z слои, w мягкость
float4 _OlSpace;     // x единиц паттерна на пиксель (пространство Screen — пункты, иначе «мир» = 100 пунктов), y «мировых» единиц на пиксель
float4 _OlGradient;  // x градиент по ширине, y сила градиента по контуру, z его скорость, w доля нового стиля (LUT)
float4 _OlWave;      // x период px, y скорость, z толщина, w сила
float4 _OlMarch;     // x штрихов, y скорость, z толщина, w сила
float4 _OlFire;      // x сила, y масштаб px, z скорость, w мерцание
float4 _OlElectric;  // x дрожание px, y масштаб px, z скорость, w разряды
float4 _OlSparkle;   // цвет искр
float4 _OlSparkle2;  // x плотность, y ячейка px, z частота
float4 _OlScan;      // цвет сканера
float4 _OlScan2;     // xy направление, z период (единицы паттерна)
float4 _OlScan3;     // x ширина, y мягкость, z скорость
float4 _OlDissolve;  // x доля, y масштаб, z кромка
float4 _OlDissolveEdge;
float4 _OlTex;       // x есть текстура паттерна, y есть текстура заливки, z тайл заливки, w сила текстуры

Texture2D _OlLut;       // 256×4: кривая внешняя (r), внутренняя (r), градиент по ширине, по контуру
Texture2D _OlLutPrev;   // то же для прежнего стиля (плавная смена), доля — _OlGradient.w
Texture2D _OlPatternTex;
Texture2D _OlFillTex;

struct OlVaryings
{
    float4 vertex : SV_POSITION;
    float2 uv : TEXCOORD0;
    nointerpolation float rectIndex : TEXCOORD1;
};

OlVaryings OlVert(FilterVertexInput v)
{
    OlVaryings o;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    o.rectIndex = (float)GetFilterRectIndex(v);
    return o;
}

struct OlRect
{
    float4 uvRect;   // прямоугольник источника в uv
    float2 texSize;  // размер текстуры источника
    float2 size;     // размер прямоугольника, px
};

OlRect OlMakeRect(OlVaryings i)
{
    OlRect r;
    r.uvRect = GetFilterUVRect((uint)(i.rectIndex + 0.5));
    uint w, h;
    _MainTex.GetDimensions(w, h);
    r.texSize = float2(w, h);
    r.size = max(r.uvRect.zw * r.texSize, 1.0);
    return r;
}

// центр пикселя фрагмента в координатах прямоугольника
float2 OlPixel(OlVaryings i, OlRect r)
{
    float2 n = (i.uv - r.uvRect.xy) / r.uvRect.zw;
    return floor(n * r.size) + 0.5;
}

// значение источника в точке p (центр пикселя); вне прямоугольника — пусто
float4 OlLoad(OlRect r, float2 p)
{
    if (any(p < 0.0) || any(p >= r.size))
        return float4(1.0, 1.0, 1.0, 0.0);
    float2 n = p / r.size;
    n.y = 1.0 - n.y; // содержимое в текстуре перевёрнуто относительно uv квада
    float2 uv = r.uvRect.xy + n * r.uvRect.zw;
    int2 t = int2(floor(uv * r.texSize));
    return _MainTex.Load(int3(t, 0));
}

bool OlOccupied(float4 v) { return v.a > OL_OCCUPIED; }

// точные sRGB-преобразования: закодированные биты должны пережить запись/чтение в sRGB-цель
float3 OlLinearToGamma(float3 c)
{
    c = max(c, 0.0);
    return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
}

float3 OlGammaToLinear(float3 c)
{
    return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

// --- смещение до сида: 12 + 12 бит в RGB (альфа 0) ---
float4 OlEncode(float2 offset, bool valid)
{
    uint2 u = valid ? (uint2)clamp(round(offset) + 2048.0, 0.0, 4094.0) : uint2(4095u, 4095u);
    float3 bits = float3(u.x & 255u, ((u.x >> 8) & 15u) | ((u.y & 15u) << 4), (u.y >> 4) & 255u) / 255.0;
    if (_OlPx.w < 0.5)
        bits = OlGammaToLinear(bits);
    return float4(bits, 0.0);
}

bool OlDecode(float4 v, out float2 offset)
{
    float3 bits = _OlPx.z < 0.5 ? OlLinearToGamma(v.rgb) : v.rgb;
    uint3 b = (uint3)round(saturate(bits) * 255.0);
    uint2 u = uint2(b.r | ((b.g & 15u) << 8), (b.g >> 4) | (b.b << 4));
    offset = float2(u) - 2048.0;
    return !(u.x == 4095u && u.y == 4095u);
}

// цвет стиля (заданный в гамме) — в пространство, в котором читается источник
float3 OlStyleColor(float3 c)
{
    return _OlPx.z > 0.5 ? c : OlGammaToLinear(c);
}

// --- шум и паттерны (как в пакете подсветки мира) ---
float OlHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float OlValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = OlHash(i);
    float b = OlHash(i + float2(1, 0));
    float c = OlHash(i + float2(0, 1));
    float d = OlHash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float OlFbm(float2 p)
{
    float v = OlValueNoise(p) * 0.5;
    v += OlValueNoise(p * 2.03 + 17.1) * 0.25;
    v += OlValueNoise(p * 4.01 + 41.7) * 0.125;
    return v / 0.875;
}

float OlStepAA(float x, float edge, float aa)
{
    return 1.0 - smoothstep(edge - aa, edge + aa, x);
}

// LUT стиля: строка row (0 внешняя кривая, 1 внутренняя, 2 градиент по ширине, 3 по контуру)
float4 OlLut(uint row, float t)
{
    float2 uv = float2((saturate(t) * 255.0 + 0.5) / 256.0, (row + 0.5) / 4.0);
    float4 a = _OlLut.SampleLevel(sampler_LinearClamp, uv, 0);
    if (_OlGradient.w < 0.999)
        a = lerp(_OlLutPrev.SampleLevel(sampler_LinearClamp, uv, 0), a, _OlGradient.w);
    return a;
}

// Паттерн в точке q (единицы паттерна), pxSize — размер пикселя в них
float OlPatternValue(float2 p, float pxSize, float time)
{
    uint type = (uint)round(_OlPattern.x);
    if (type == 0u)
        return 1.0;
    float scale = max(_OlPattern.y, 1e-4);
    float s, c;
    sincos(_OlPattern.z, s, c);
    float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y) / scale;
    q.x += time * _OlPattern.w;

    float duty = saturate(_OlPattern2.x);
    float aa = max(pxSize / scale, 1e-3) * max(_OlPattern2.w, 0.5);

    if (type == 8u)
    {
        if (_OlTex.x < 0.5)
            return 1.0;
        float lod = log2(max(256.0 * pxSize / scale, 1.0));
        float4 t = _OlPatternTex.SampleLevel(sampler_LinearRepeat, q, lod);
        return dot(t.rgb, float3(0.299, 0.587, 0.114)) * t.a;
    }
    if (type == 1u)
        return OlStepAA(frac(q.x), duty, aa);
    if (type == 2u)
    {
        float2 f = frac(q) - 0.5;
        return OlStepAA(length(f), duty * 0.5, aa);
    }
    if (type == 3u)
    {
        float2 f = abs(frac(q) - 0.5);
        return OlStepAA(0.5 - max(f.x, f.y), duty * 0.5, aa);
    }
    if (type == 4u)
    {
        float2 f = frac(q * 0.5) - 0.5;
        return smoothstep(-aa * 0.25, aa * 0.25, f.x * f.y);
    }
    if (type == 5u)
        return lerp(1.0 - duty, 1.0, 0.5 + 0.5 * sin(q.y * OL_TWO_PI));
    if (type == 6u)
        return smoothstep(1.0 - duty - 0.15, 1.0 - duty + 0.15, OlValueNoise(q));
    {
        const float2 rr = float2(1.0, 1.7320508);
        float2 h = rr * 0.5;
        float2 a = (q - rr * floor(q / rr)) - h;
        float2 qb = q - h;
        float2 b = (qb - rr * floor(qb / rr)) - h;
        float2 g = dot(a, a) < dot(b, b) ? a : b;
        float edgeDist = 0.5 - max(abs(g.x) * 0.5 + abs(g.y) * 0.8660254, abs(g.x));
        return OlStepAA(edgeDist, duty * 0.25, aa);
    }
}

// множитель паттерна для слоя (1 внешний, 2 внутренний, 4 заливка)
float OlPatternFor(uint layerBit, float2 p, float time)
{
    if (_OlPattern.x < 0.5)
        return 1.0;
    if ((((uint)round(_OlPattern2.z)) & layerBit) == 0u)
        return 1.0;
    float2 q = p * _OlSpace.x;
    return lerp(1.0, OlPatternValue(q, _OlSpace.x, time), saturate(_OlPattern2.y));
}

void OlOver(inout float4 acc, float3 color, float a)
{
    acc.rgb = color * a + acc.rgb * (1.0 - a);
    acc.a = a + acc.a * (1.0 - a);
}

#endif
