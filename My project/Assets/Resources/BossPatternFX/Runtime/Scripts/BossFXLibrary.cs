using UnityEngine;

namespace BossFX
{
    /// <summary>도형 종류. 셰이더의 _Shape 값과 숫자가 일치해야 합니다.</summary>
    public enum BossShape { Circle = 0, Ring = 1, Cone = 2, Line = 3 }

    /// <summary>채워지는 방향. 셰이더의 _FillMode 와 일치.</summary>
    public enum BossFillMode { Radial = 0, Angular = 1, Linear = 2 }

    /// <summary>BossFX/Radial 셰이더의 _Mode 와 일치.</summary>
    public enum BossRadialMode { Ring = 0, Orb = 1, Burst = 2 }

    /// <summary>
    /// 메시 / 머티리얼 / 노이즈 텍스처를 런타임에 만들어 공유합니다.
    /// 프리팹이나 에셋을 미리 만들어 둘 필요가 없도록 하는 것이 목적입니다.
    /// </summary>
    public static class BossFXLibrary
    {
        // ---- 셰이더 프로퍼티 ID (문자열 조회 비용 제거) --------------------
        public static readonly int PShape         = Shader.PropertyToID("_Shape");
        public static readonly int PInnerRadius   = Shader.PropertyToID("_InnerRadius");
        public static readonly int PConeAngle     = Shader.PropertyToID("_ConeAngle");
        public static readonly int PConeDirection = Shader.PropertyToID("_ConeDirection");
        public static readonly int PLineWidth     = Shader.PropertyToID("_LineWidth");
        public static readonly int PFillMode      = Shader.PropertyToID("_FillMode");
        public static readonly int PFill          = Shader.PropertyToID("_Fill");
        public static readonly int PColorBase     = Shader.PropertyToID("_ColorBase");
        public static readonly int PColorHot      = Shader.PropertyToID("_ColorHot");
        public static readonly int PColorEdge     = Shader.PropertyToID("_ColorEdge");
        public static readonly int PColorCore     = Shader.PropertyToID("_ColorCore");
        public static readonly int PIntensity     = Shader.PropertyToID("_Intensity");
        public static readonly int POpacity       = Shader.PropertyToID("_Opacity");
        public static readonly int PRadius        = Shader.PropertyToID("_Radius");
        public static readonly int PThickness     = Shader.PropertyToID("_Thickness");
        public static readonly int PMode          = Shader.PropertyToID("_Mode");
        public static readonly int PCharge        = Shader.PropertyToID("_Charge");
        public static readonly int PFire          = Shader.PropertyToID("_Fire");
        public static readonly int PNoiseTex      = Shader.PropertyToID("_NoiseTex");
        public static readonly int PFalloff       = Shader.PropertyToID("_Falloff");
        public static readonly int PColorGlow     = Shader.PropertyToID("_ColorGlow");

        // ---- 색상 팔레트 (챔버 레퍼런스에서 뽑은 값) -----------------------
        public static readonly Color Purple = new Color(0.55f, 0.20f, 1.00f, 1f);
        public static readonly Color Violet = new Color(0.74f, 0.44f, 1.00f, 1f);
        public static readonly Color Blue   = new Color(0.24f, 0.52f, 1.00f, 1f);
        public static readonly Color Cyan   = new Color(0.52f, 0.80f, 1.00f, 1f);
        public static readonly Color Red    = new Color(1.00f, 0.10f, 0.10f, 1f);
        public static readonly Color CoreHot = new Color(1.00f, 0.70f, 1.00f, 1f);

        static Mesh _quadXZ;
        static Mesh _quadForward;
        static Texture2D _noise;

        /// <summary>XZ 평면 1x1 쿼드 (윗면이 +Y). 바닥 장판용.</summary>
        public static Mesh QuadXZ
        {
            get
            {
                if (_quadXZ == null)
                {
                    _quadXZ = new Mesh { name = "BossFX_QuadXZ" };
                    _quadXZ.vertices = new[]
                    {
                        new Vector3(-0.5f, 0f, -0.5f),
                        new Vector3( 0.5f, 0f, -0.5f),
                        new Vector3( 0.5f, 0f,  0.5f),
                        new Vector3(-0.5f, 0f,  0.5f),
                    };
                    // u → local Z, v → local X 로 매핑합니다.
                    // 셰이더에서 p.x = local Z(= transform.forward), p.y = local X 가 되어
                    // 부채꼴 기본 방향과 직선 진행 방향이 transform.forward 와 일치합니다.
                    _quadXZ.uv = new[]
                    {
                        new Vector2(0f, 0f), new Vector2(0f, 1f),
                        new Vector2(1f, 1f), new Vector2(1f, 0f),
                    };
                    _quadXZ.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                    _quadXZ.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                    _quadXZ.RecalculateBounds();
                }
                return _quadXZ;
            }
        }

        /// <summary>
        /// +X 방향으로 뻗는 쿼드. 원점이 왼쪽 끝에 있어서
        /// 오브젝트 위치에서 앞으로 뻗어나가는 빔에 적합합니다.
        /// x: 0..1, y: -0.5..0.5
        /// </summary>
        public static Mesh QuadForward
        {
            get
            {
                if (_quadForward == null)
                {
                    _quadForward = new Mesh { name = "BossFX_QuadForward" };
                    _quadForward.vertices = new[]
                    {
                        new Vector3(0f, -0.5f, 0f),
                        new Vector3(1f, -0.5f, 0f),
                        new Vector3(1f,  0.5f, 0f),
                        new Vector3(0f,  0.5f, 0f),
                    };
                    _quadForward.uv = new[]
                    {
                        new Vector2(0f, 0f), new Vector2(1f, 0f),
                        new Vector2(1f, 1f), new Vector2(0f, 1f),
                    };
                    _quadForward.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                    _quadForward.normals = new[]
                    {
                        -Vector3.forward, -Vector3.forward,
                        -Vector3.forward, -Vector3.forward
                    };
                    _quadForward.RecalculateBounds();
                }
                return _quadForward;
            }
        }

        /// <summary>
        /// 런타임 생성 심리스 밸류 노이즈. 텍스처 에셋을 안 넣어도 되도록.
        /// (Textures 폴더의 PNG 를 직접 할당하면 그쪽이 우선입니다)
        /// </summary>
        public static Texture2D Noise
        {
            get
            {
                if (_noise == null) _noise = GenerateNoise(128, 4, 1337);
                return _noise;
            }
        }

        static Texture2D GenerateNoise(int size, int cells, int seed)
        {
            var rnd = new System.Random(seed);
            // 격자점 값 (경계를 감아서 심리스로)
            var grid = new float[cells, cells];
            for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                    grid[x, y] = (float)rnd.NextDouble();

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "BossFX_Noise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float v = 0f, amp = 0.5f, freq = 1f;
                    for (int o = 0; o < 3; o++)          // 옥타브 3개
                    {
                        v += SampleGrid(grid, cells, x / (float)size * freq,
                                        y / (float)size * freq) * amp;
                        amp *= 0.5f;
                        freq *= 2f;
                    }
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f * 1.35f), 0, 255);
                    px[y * size + x] = new Color32(b, b, b, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        static float SampleGrid(float[,] grid, int cells, float u, float v)
        {
            u *= cells; v *= cells;
            int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
            float fx = u - x0, fy = v - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int x1 = x0 + 1, y1 = y0 + 1;
            // 감아서 심리스
            x0 = ((x0 % cells) + cells) % cells; x1 = ((x1 % cells) + cells) % cells;
            y0 = ((y0 % cells) + cells) % cells; y1 = ((y1 % cells) + cells) % cells;
            float a = Mathf.Lerp(grid[x0, y0], grid[x1, y0], fx);
            float b = Mathf.Lerp(grid[x0, y1], grid[x1, y1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// <summary>셰이더 이름으로 머티리얼 생성. 없으면 경고 후 null.</summary>
        public static Material CreateMaterial(string shaderName)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null)
            {
                Debug.LogError($"[BossFX] 셰이더를 찾을 수 없습니다: {shaderName}\n" +
                               "Shaders 폴더가 프로젝트 안에 있는지, URP 프로젝트가 맞는지 확인하세요.");
                return null;
            }
            var mat = new Material(sh) { name = shaderName.Replace('/', '_') };
            if (mat.HasProperty(PNoiseTex) && mat.GetTexture(PNoiseTex) == null)
                mat.SetTexture(PNoiseTex, Noise);
            return mat;
        }
    }
}
