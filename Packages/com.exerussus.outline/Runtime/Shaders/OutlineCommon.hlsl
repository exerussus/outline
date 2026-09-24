#ifndef EXERUSSUS_OUTLINE_COMMON_INCLUDED
#define EXERUSSUS_OUTLINE_COMMON_INCLUDED

// Общие объявления полноэкранных проходов подсветки (JFA + композит).
// Раскладка столбцов _OutlineData совпадает с OutlineGpuTables.cs.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

#define OL_COL_OUTER    0
#define OL_COL_INNER    1
#define OL_COL_FILL     2
#define OL_COL_RIM      3
#define OL_COL_WIDTHS   4   // x outerPx, y innerPx, z rimPower, w group
#define OL_COL_MISC     5   // x fade, y additive, z priorityBias, w occludedMode
#define OL_COL_OCC      6   // тинт перекрытой части
#define OL_COL_OCC2     7   // x dashPeriod, y dashDuty, z dashSpeed, w occludedInnerMul
#define OL_COL_PULSE    8   // x speed, y alpha, z width
#define OL_COL_NOISE    9   // x scale, y amount, z speed, w 1/outerWidth в px поля
#define OL_COL_PATTERN  10  // x тип, y масштаб px, z угол (рад), w скорость
#define OL_COL_PATTERN2 11  // x заполнение, y сила, z слои (1 внешний, 2 внутренний, 4 заливка), w мягкость
#define OL_COL_PATSPACE 12  // x пространство (0 экран, 1 объект, 2 поверхность объекта, 3 поверхность мира),
                            // yz якорь на экране, px кадра; w пикселей на мировую единицу у якоря
#define OL_COL_GRADIENT 13  // x цвет свечения из градиента, y сила градиента по контуру, z его скорость
#define OL_COL_WAVE     14  // x период px, y скорость, z толщина, w сила
#define OL_COL_MARCH    15  // x штрихов на оборот, y скорость, z толщина, w сила
#define OL_COL_FIRE     16  // x сила, y масштаб px, z скорость, w мерцание
#define OL_COL_ELECTRIC 17  // x дрожание px, y масштаб px, z скорость, w разряды
#define OL_COL_SPARKLE  18  // цвет искр (a — яркость)
#define OL_COL_SPARKLE2 19  // x плотность, y ячейка px, z частота
#define OL_COL_SCAN     20  // цвет сканера (a — сила)
#define OL_COL_SCAN2    21  // xyz направление, w период
#define OL_COL_SCAN3    22  // x ширина, y мягкость, z скорость
#define OL_COL_DISSOLVE 23  // x доля, y масштаб, z кромка, w по fade
#define OL_COL_DISSOLVE_EDGE 24
#define OL_COL_TEXTURES 25  // x слой текстуры паттерна (-1 нет), y слой текстуры заливки, z тайл заливки, w её сила
#define OL_COL_SEETHRU  26  // x вкл, y непрозрачность объекта, z искажение px, w масштаб искажения px
#define OL_COL_SEETHRU2 27  // x скорость искажения, y преломление px, z ширина зоны у края px
#define OL_COL_SEETINT  28  // тинт фона (a — сила)
#define OL_COL_SHIMMER  29  // мерцание края (a — сила)

#define OL_LUT_ROWS 128.0
#define OL_INVALID_PENALTY 1e6

TEXTURE2D(_OutlineMask);          // RGBA8: r = id/255, g = rim, b = видим, a = покрытие
TEXTURE2D(_OutlineSeeds);         // внешнее поле: координаты сида В ПИКСЕЛЯХ КАДРА (12+12 бит) + id
TEXTURE2D(_OutlineSeedsInner);    // внутреннее поле (обычное расстояние до края)
TEXTURE2D_FLOAT(_OutlineData);    // 16×64 float4
TEXTURE2D(_OutlineLut);           // 256×128 R8
TEXTURE2D(_OutlineRamp);          // 256×128 RGBAHalf: id*2 — цвет по ширине свечения, id*2+1 — по углу
TEXTURE2D_ARRAY(_OutlineTexArray); // текстуры стилей 256×256 с мипами
TEXTURE2D_FLOAT(_OutlinePos);     // RG: координата развёртки поверхности по доминирующей оси нормали;
                                  // есть, только если запись с паттерном Surface* попала в кадр

float4 _OutlineMaskSize;  // xy размер маски (= кадр), zw обратный
float4 _OutlineSeedSize;  // xy размер поля, z масштаб поля (поле/кадр), w обратный
float4 _OutlineParams;    // x время, y шаг JFA, z радиус мягкого стыка (px кадра), w режим отладки
float4 _OutlineParams2;   // x наложение на стыке групп, y есть внутреннее поле, z наибольшая дальность свечения, px кадра
float4 _OutlineRect;      // область работы в px поля: xy min (вкл.), zw max (искл.)

// Горячие параметры записей для JFA/композита — uniform-массив вместо выборок из текстуры данных:
// x = 1/ширина в px поля (0 — запись погашена), y = смещение приоритета, z = группа, w = 1 если запись есть
float4 _OutlineEntries[64];

float OutlineEntryInvW(uint id) { return _OutlineEntries[id].x; }
float OutlineEntryPrio(uint id) { return _OutlineEntries[id].y; }
float OutlineEntryGroup(uint id) { return _OutlineEntries[id].z; }

struct OutlineVaryings
{
    float4 positionCS : SV_POSITION;
};

// Полноэкранный треугольник без меша (DrawProcedural, 3 вершины).
OutlineVaryings OutlineFullscreenVert(uint vertexID : SV_VertexID)
{
    OutlineVaryings o;
    o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
    return o;
}

float4 OutlineData(uint id, uint column)
{
    return LOAD_TEXTURE2D(_OutlineData, uint2(column, id));
}

float4 OutlineLoadMask(int2 p)
{
    p = clamp(p, int2(0, 0), int2(_OutlineMaskSize.xy) - 1);
    return LOAD_TEXTURE2D(_OutlineMask, uint2(p));
}

uint OutlineMaskId(float4 m)
{
    return (uint)round(m.r * 255.0);
}

// центр пикселя поля в пикселях кадра
float2 OutlineFieldToFrame(int2 pf)
{
    return (float2(pf) + 0.5) * _OutlineSeedSize.w;
}

bool OutlineInRect(int2 pf)
{
    return all(pf >= int2(_OutlineRect.xy)) && all(pf < int2(_OutlineRect.zw));
}

// --- упаковка сида в RGBA8: x — 12 бит, y — 12 бит, id — 8 бит (0 = нет сида) ---
float4 OutlineEncodeSeed(uint2 pos, uint id)
{
    uint r = pos.x & 255u;
    uint g = ((pos.x >> 8) & 15u) | ((pos.y & 15u) << 4);
    uint b = (pos.y >> 4) & 255u;
    return float4(r, g, b, id) / 255.0;
}

void OutlineDecodeSeed(float4 v, out uint2 pos, out uint id)
{
    uint4 u = (uint4)round(v * 255.0);
    pos.x = u.r | ((u.g & 15u) << 8);
    pos.y = (u.g >> 4) | (u.b << 4);
    id = u.a;
}

float OutlineLut(uint row, float t)
{
    float2 uv = float2((saturate(t) * 255.0 + 0.5) / 256.0, (row + 0.5) / OL_LUT_ROWS);
    return SAMPLE_TEXTURE2D_LOD(_OutlineLut, sampler_LinearClamp, uv, 0).r;
}

float3 OutlineIdColor(uint id)
{
    // хэш id → оттенок, для отладки
    float h = frac(id * 0.618034 + 0.13);
    float3 c = saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    return lerp(0.25, 1.0, c);
}

float OutlineHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float OutlineValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = OutlineHash(i);
    float b = OutlineHash(i + float2(1, 0));
    float c = OutlineHash(i + float2(0, 1));
    float d = OutlineHash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float OutlineFbm(float2 p)
{
    float v = OutlineValueNoise(p) * 0.5;
    v += OutlineValueNoise(p * 2.03 + 17.1) * 0.25;
    v += OutlineValueNoise(p * 4.01 + 41.7) * 0.125;
    return v / 0.875;
}

float4 OutlineRamp(uint row, float t)
{
    float2 uv = float2((saturate(t) * 255.0 + 0.5) / 256.0, (row + 0.5) / OL_LUT_ROWS);
    return SAMPLE_TEXTURE2D_LOD(_OutlineRamp, sampler_LinearClamp, uv, 0);
}

// Выборка текстуры стиля: uv в тайлах, pxPerTile — сколько пикселей экрана приходится на тайл (для мипа)
float4 OutlineStyleTexture(float slice, float2 uv, float pxPerTile)
{
    float lod = log2(max(256.0 / max(pxPerTile, 1e-3), 1.0));
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_OutlineTexArray, sampler_LinearRepeat, uv, slice, lod);
}

// Мягкая ступенька: 1 при x < edge, 0 при x > edge, переход шириной aa
float OutlineStepAA(float x, float edge, float aa)
{
    return 1.0 - smoothstep(edge - aa, edge + aa, x);
}

// --- паттерны (экранное пространство) ---
// тип: 0 нет, 1 полосы, 2 точки, 3 сетка, 4 клетка, 5 скан-линии, 6 шум, 7 соты, 8 текстура
float OutlinePatternValue(float2 p, float pxSize, float4 pat, float4 pat2, float time, float texSlice)
{
    uint type = (uint)round(pat.x);
    if (type == 0u)
        return 1.0;

    // p и период — в одних единицах (px для экрана, мировые для остальных); pxSize — размер пикселя в них
    float scale = max(pat.y, 1e-4);
    float s, c;
    sincos(pat.z, s, c);
    float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y) / scale;
    q.x += time * pat.w;

    float duty = saturate(pat2.x);
    float aa = max(pxSize / scale, 1e-3) * max(pat2.w, 0.5);

    if (type == 8u) // своя текстура: яркость × альфа
    {
        if (texSlice < 0.0)
            return 1.0;
        float4 t = OutlineStyleTexture(texSlice, q, scale / max(pxSize, 1e-5));
        return dot(t.rgb, float3(0.299, 0.587, 0.114)) * t.a;
    }
    if (type == 1u) // полосы
        return OutlineStepAA(frac(q.x), duty, aa);
    if (type == 2u) // точки
    {
        float2 f = frac(q) - 0.5;
        return OutlineStepAA(length(f), duty * 0.5, aa);
    }
    if (type == 3u) // сетка
    {
        float2 f = abs(frac(q) - 0.5);
        float line_ = 0.5 - max(f.x, f.y);
        return OutlineStepAA(line_, duty * 0.5, aa);
    }
    if (type == 4u) // клетка
    {
        float2 f = frac(q * 0.5) - 0.5;
        float v = f.x * f.y;
        return smoothstep(-aa * 0.25, aa * 0.25, v);
    }
    if (type == 5u) // скан-линии: мягкая синусоида поперёк
        return lerp(1.0 - duty, 1.0, 0.5 + 0.5 * sin(q.y * TWO_PI));
    if (type == 6u) // шум с порогом
        return smoothstep(1.0 - duty - 0.15, 1.0 - duty + 0.15, OutlineValueNoise(q));
    // 7 — соты: расстояние до центра ближайшей шестиугольной ячейки
    {
        const float2 r = float2(1.0, 1.7320508);
        float2 h = r * 0.5;
        float2 a = (q - r * floor(q / r)) - h;
        float2 qb = q - h;
        float2 b = (qb - r * floor(qb / r)) - h;
        float2 g = dot(a, a) < dot(b, b) ? a : b;
        float edgeDist = 0.5 - max(abs(g.x) * 0.5 + abs(g.y) * 0.8660254, abs(g.x));
        return OutlineStepAA(edgeDist, duty * 0.25, aa);
    }
}

// Координаты записи id в пространстве её паттерна: q — в единицах пространства (px для Screen, мировые
// или объекта для остальных), pxSize — размер пикселя кадра в этих единицах, pos — позиция поверхности.
// onSurface — пиксель внутри силуэта записи: только там есть позиция поверхности для Surface*.
// Свечение снаружи в режимах Surface* строится как в Object — привязкой к якорю на экране.
void OutlineSpaceCoords(uint id, float2 p, bool onSurface, out float2 q, out float pxSize, out float3 pos, out bool hasPos)
{
    float4 sp = OutlineData(id, OL_COL_PATSPACE);
    uint space = (uint)round(sp.x);
    q = p;
    pxSize = 1.0;
    pos = 0;
    hasPos = false;
    if (space == 0u)
        return;
    pxSize = 1.0 / max(sp.w, 1e-3);
    if (space >= 2u && onSurface)
    {
        q = LOAD_TEXTURE2D(_OutlinePos, uint2(p)).xy;
        // координаты пишутся без MSAA: пиксель края, центр которого не покрыт, остался пустым (0) — берём соседа
        if (all(q == 0.0))
        {
            int2 ip = int2(p);
            float2 n0 = LOAD_TEXTURE2D(_OutlinePos, uint2(ip + int2(1, 0))).xy;
            float2 n1 = LOAD_TEXTURE2D(_OutlinePos, uint2(max(ip + int2(-1, 0), 0))).xy;
            float2 n2 = LOAD_TEXTURE2D(_OutlinePos, uint2(ip + int2(0, 1))).xy;
            float2 n3 = LOAD_TEXTURE2D(_OutlinePos, uint2(max(ip + int2(0, -1), 0))).xy;
            q = any(n0 != 0.0) ? n0 : (any(n1 != 0.0) ? n1 : (any(n2 != 0.0) ? n2 : n3));
        }
        hasPos = true;
    }
    else
    {
        q = (p - sp.yz) * pxSize;
    }
}

// Множитель паттерна для слоя (layerBit: 1 внешний, 2 внутренний, 4 заливка) в точке кадра p.
float OutlinePatternFor(uint id, uint layerBit, float2 p, float time, bool onSurface)
{
    float4 pat = OutlineData(id, OL_COL_PATTERN);
    if (pat.x < 0.5)
        return 1.0;
    float4 pat2 = OutlineData(id, OL_COL_PATTERN2);
    if ((((uint)round(pat2.z)) & layerBit) == 0u)
        return 1.0;

    float2 q;
    float pxSize;
    float3 pos;
    bool hasPos;
    OutlineSpaceCoords(id, p, onSurface, q, pxSize, pos, hasPos);
    float slice = OutlineData(id, OL_COL_TEXTURES).x;
    return lerp(1.0, OutlinePatternValue(q, pxSize, pat, pat2, time, slice), saturate(pat2.y));
}

#endif
