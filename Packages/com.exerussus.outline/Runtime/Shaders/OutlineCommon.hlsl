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

#define OL_LUT_ROWS 128.0
#define OL_INVALID_PENALTY 1e6

TEXTURE2D(_OutlineMask);          // RGBA8: r = id/255, g = rim, b = видим, a = покрытие
TEXTURE2D(_OutlineSeeds);         // внешнее поле: координаты сида В ПИКСЕЛЯХ КАДРА (12+12 бит) + id
TEXTURE2D(_OutlineSeedsInner);    // внутреннее поле (обычное расстояние до края)
TEXTURE2D_FLOAT(_OutlineData);    // 16×64 float4
TEXTURE2D(_OutlineLut);           // 256×128 R8

float4 _OutlineMaskSize;  // xy размер маски (= кадр), zw обратный
float4 _OutlineSeedSize;  // xy размер поля, z масштаб поля (поле/кадр), w обратный
float4 _OutlineParams;    // x время, y шаг JFA, z радиус мягкого стыка (px кадра), w режим отладки
float4 _OutlineParams2;   // x наложение на стыке групп, y есть внутреннее поле
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

// Мягкая ступенька: 1 при x < edge, 0 при x > edge, переход шириной aa
float OutlineStepAA(float x, float edge, float aa)
{
    return 1.0 - smoothstep(edge - aa, edge + aa, x);
}

// --- паттерны (экранное пространство) ---
// тип: 0 нет, 1 полосы, 2 точки, 3 сетка, 4 клетка, 5 скан-линии, 6 шум, 7 соты
float OutlinePatternValue(float2 p, float4 pat, float4 pat2, float time)
{
    uint type = (uint)round(pat.x);
    if (type == 0u)
        return 1.0;

    float scale = max(pat.y, 1.0);
    float s, c;
    sincos(pat.z, s, c);
    float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y) / scale;
    q.x += time * pat.w;

    float duty = saturate(pat2.x);
    float aa = max(1.0 / scale, 1e-3) * max(pat2.w, 0.5);

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

// множитель паттерна для слоя (layerBit: 1 внешний, 2 внутренний, 4 заливка)
float OutlinePatternFor(uint id, uint layerBit, float2 p, float time)
{
    float4 pat = OutlineData(id, OL_COL_PATTERN);
    if (pat.x < 0.5)
        return 1.0;
    float4 pat2 = OutlineData(id, OL_COL_PATTERN2);
    if ((((uint)round(pat2.z)) & layerBit) == 0u)
        return 1.0;
    return lerp(1.0, OutlinePatternValue(p, pat, pat2, time), saturate(pat2.y));
}

#endif
