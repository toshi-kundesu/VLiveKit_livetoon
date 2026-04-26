#ifdef MYHLSLINCLUDE_INCLUDED
#define MYHLSLINCLUDE_INCLUDED

void MyFunction_float(float3 A, float3 B, out float3 Out)
{
    Out = A + B;
}

#endif