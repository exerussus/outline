#ifndef EXERUSSUS_OUTLINE_UI_FILTER_INCLUDED
#define EXERUSSUS_OUTLINE_UI_FILTER_INCLUDED

// Фильтр подсветки UI Toolkit. Поддерево элемента растеризовано в текстуру (_MainTex); проходы фильтра идут
// цепочкой — каждый читает выход предыдущего. Внутри силуэта хранится исходный цвет, снаружи — метка
// расстояния до силуэта (точное евклидово расстояние в два разделимых прохода: по строке, затем по столбцу).
// Координаты p — в пикселях прямоугольника фильтра; ось y идёт вниз по экрану.

#include "UnityCG.cginc"
#include "UnityUIEFilter.cginc"

#define OL_TWO_PI 6.28318530718
#define OL_OCCUPIED 0.002

Texture2D _MainTex;
SamplerState sampler_LinearClamp;
SamplerState sampler_LinearRepeat;

// заполняются из C# на каждый проход (OutlineUiFilter.ApplySettings)
float4 _OlPx;        // x пикселей на пункт, y время, z источник в гамме, w выход в гамме
float4 _OlDebug;     // x отладочный вид (OutlineUiDebugView)
float4 _OlStep;      // x наибольшее расстояние поля R, px
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
    bool flip;       // перевернуть источник по y
};

// flip: переворот источника по y. Проверено на 6000.6: источник каждого прохода (и контент элемента, и выходы
// наших проходов) уже в ориентации квада — переворот из примера Unity здесь переворачивает картинку; флаг оставлен
// на случай другой платформы/версии.
OlRect OlMakeRect(OlVaryings i, bool flip)
{
    OlRect r;
    r.flip = flip;
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
    if (r.flip)
        n.y = 1.0 - n.y;
    float2 uv = r.uvRect.xy + n * r.uvRect.zw;
    int2 t = int2(floor(uv * r.texSize));
    return _MainTex.Load(int3(t, 0));
}

bool OlIsMark(float4 v);
// пиксель силуэта: исходный контент (не метка расстояния)
bool OlOccupied(float4 v) { return v.a > OL_OCCUPIED && !OlIsMark(v); }

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

// --- расстояние снаружи силуэта: метка в RGB (только 0 и 1 — не меняются при sRGB-преобразованиях) + значение
// в альфе (альфа никогда не гамма-кодируется). Premultiplied-контент не может иметь канал 1 при альфе < 1,
// поэтому метки не путаются с содержимым. DATA = (1, 0, 1, d / (R + 1)), NONE = (1, 1, 0, 0).
float4 OlEncode(float d, bool valid)
{
    if (!valid)
        return float4(1.0, 1.0, 0.0, 0.0);
    float r = max(_OlStep.x, 1.0) + 1.0;
    return float4(1.0, 0.0, 1.0, clamp(d / r, 0.0, 0.996));
}

bool OlIsMark(float4 v) { return v.r > 0.99 && v.a < 0.9975 && (v.g < 0.01 || v.g > 0.99) && (v.b < 0.01 || v.b > 0.99) && v.g + v.b > 0.99 && v.g + v.b < 1.01; }

// true — у пикселя есть расстояние (d, px); false — «до края дальше R» или не метка
bool OlDecode(float4 v, out float d)
{
    d = 0.0;
    if (!OlIsMark(v) || v.g > 0.5)
        return false;
    d = v.a * (max(_OlStep.x, 1.0) + 1.0);
    return true;
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
