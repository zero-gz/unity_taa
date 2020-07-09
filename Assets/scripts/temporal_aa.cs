using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class temporal_aa : MonoBehaviour {
    // camera jitter
    public float patternScale = 1.0f;
    public Vector4 activeSample = Vector4.zero;// xy = current sample, zw = previous sample
    public int activeIndex = -2;
    public bool useMotionBlur = true;
    public bool useClosestDepth = true;
    public bool useAddNoise = true;

    private static float[] points_Halton_2_3_x16 = new float[16 * 2];
    private Camera _camera;
    private Vector3 focalMotionPos = Vector3.zero;
    private Vector3 focalMotionDir = Vector3.right;

    // motion vector part
    private RenderTexture velocityBuffer;
    private bool paramInitialized = false;
    private Vector4 paramProjectionExtents;
    private Matrix4x4 paramCurrV;
    private Matrix4x4 paramCurrVP;
    private Matrix4x4 paramPrevVP;
    private Matrix4x4 paramPrevVP_NoFlip;
    private Material velocityMaterial;

    // main taa part
    private RenderTexture pre_tex;
    private Material m_mat;
    [Range(0.0f, 1.0f)] public float feedbackMin = 0.88f;
    [Range(0.0f, 1.0f)] public float feedbackMax = 0.97f;

    // ---------------------------------------------------------------------------------------------
    // http://en.wikipedia.org/wiki/Halton_sequence
    private static float HaltonSeq(int prime, int index = 1/* NOT! zero-based */)
    {
        float r = 0.0f;
        float f = 1.0f;
        int i = index;
        while (i > 0)
        {
            f /= prime;
            r += f * (i % prime);
            i = (int)Mathf.Floor(i / (float)prime);
        }
        return r;
    }

    private static void InitializeHalton_2_3(float[] seq)
    {
        for (int i = 0, n = seq.Length / 2; i != n; i++)
        {
            float u = HaltonSeq(2, i + 1) - 0.5f;
            float v = HaltonSeq(3, i + 1) - 0.5f;
            seq[2 * i + 0] = u;
            seq[2 * i + 1] = v;
        }
    }

    private static float[] AccessPointData()
    {
        return points_Halton_2_3_x16;
    }

    public static int AccessLength()
    {
        return AccessPointData().Length / 2;
    }

    public Vector2 Sample(int index)
    {
        float[] points = AccessPointData();
        int n = points.Length / 2;
        int i = index % n;

        float x = patternScale * points[2 * i + 0];
        float y = patternScale * points[2 * i + 1];

        return new Vector2(x, y);
    }

    // jitter的部分
    void OnPreCull()
    {
        if (activeIndex == -2)
        {
            activeSample = Vector4.zero;
            activeIndex += 1;

            _camera.projectionMatrix = _camera.GetProjectionMatrix();
        }
        else
        {
            activeIndex += 1;
            activeIndex %= AccessLength();

            Vector2 sample = Sample(activeIndex);
            activeSample.z = activeSample.x;
            activeSample.w = activeSample.y;
            activeSample.x = sample.x;
            activeSample.y = sample.y;

            _camera.projectionMatrix = _camera.GetProjectionMatrix(sample.x, sample.y);
            //Debug.Log(string.Format("get camera projection matrix: %s", _camera.projectionMatrix));
        }
    }

    // --------------------------------------------------------------------------------
    void initMaterial()
    {
        velocityMaterial = new Material(Shader.Find("custom_taa/motion_vector"));

        velocityBuffer = new RenderTexture(Screen.width, Screen.height, 16, RenderTextureFormat.RGFloat);
        velocityBuffer.filterMode = FilterMode.Point;
        velocityBuffer.name = "custom_motion_vector";

        m_mat = new Material(Shader.Find("custom_taa/taa_main"));
        pre_tex = new RenderTexture(Screen.width, Screen.height, 0);
        pre_tex.name = "pre_texture";

        Shader.SetGlobalTexture(Shader.PropertyToID("_PrevTex"), pre_tex);
    }

    public void DrawFullscreenQuad()
    {
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        {
            GL.MultiTexCoord2(0, 0.0f, 0.0f);
            GL.Vertex3(0.0f, 0.0f, 0.0f); // BL

            GL.MultiTexCoord2(0, 1.0f, 0.0f);
            GL.Vertex3(1.0f, 0.0f, 0.0f); // BR

            GL.MultiTexCoord2(0, 1.0f, 1.0f);
            GL.Vertex3(1.0f, 1.0f, 0.0f); // TR

            GL.MultiTexCoord2(0, 0.0f, 1.0f);
            GL.Vertex3(0.0f, 1.0f, 0.0f); // TL
        }
        GL.End();
        GL.PopMatrix();
    }

    void OnPostRender()
    {
        int bufferW = _camera.pixelWidth;
        int bufferH = _camera.pixelHeight;

        {
            Matrix4x4 currV = _camera.worldToCameraMatrix;
            Matrix4x4 currP = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, true);
            Matrix4x4 currP_NoFlip = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, false);
            Matrix4x4 prevV = paramInitialized ? paramCurrV : currV;

            paramInitialized = true;
            paramProjectionExtents = _camera.GetProjectionExtents(activeSample.x, activeSample.y);
            paramCurrV = currV;
            paramCurrVP = currP * currV;
            paramPrevVP = currP * prevV;
            paramPrevVP_NoFlip = currP_NoFlip * prevV;
        }

        RenderTexture activeRT = RenderTexture.active;
        RenderTexture.active = velocityBuffer;
        {
            GL.Clear(true, true, Color.black);

            const int kVertices = 0;
            const int kVerticesSkinned = 1;

            velocityMaterial.SetVector("_ProjectionExtents", paramProjectionExtents);
            velocityMaterial.SetMatrix("_CurrV", paramCurrV);
            velocityMaterial.SetMatrix("_CurrVP", paramCurrVP);
            velocityMaterial.SetMatrix("_PrevVP", paramPrevVP);
            velocityMaterial.SetMatrix("_PrevVP_NoFlip", paramPrevVP_NoFlip);

            // obj process
            var obs = VelocityBufferTag.activeObjects;
            for (int i = 0, n = obs.Count; i != n; i++)
            {
                var ob = obs[i];
                if (ob != null && ob.rendering && ob.mesh != null)
                {
                    velocityMaterial.SetMatrix("_CurrM", ob.localToWorldCurr);
                    velocityMaterial.SetMatrix("_PrevM", ob.localToWorldPrev);
                    velocityMaterial.SetPass(ob.meshSmrActive ? kVerticesSkinned : kVertices);

                    for (int j = 0; j != ob.mesh.subMeshCount; j++)
                    {
                        Graphics.DrawMeshNow(ob.mesh, Matrix4x4.identity, j);
                    }
                }
            }

        }
        RenderTexture.active = activeRT;
    }

    public void EnsureKeyword(Material material, string name, bool enabled)
    {
        if (enabled != material.IsKeywordEnabled(name))
        {
            if (enabled)
                material.EnableKeyword(name);
            else
                material.DisableKeyword(name);
        }
    }

    // taa main part
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Vector4 jitterUV = activeSample;
        jitterUV.x /= source.width;
        jitterUV.y /= source.height;
        jitterUV.z /= source.width;
        jitterUV.w /= source.height;

        m_mat.SetVector("_JitterUV", jitterUV);
        m_mat.SetTexture("_VelocityBuffer", velocityBuffer);
        m_mat.SetFloat("_FeedbackMin", feedbackMin);
        m_mat.SetFloat("_FeedbackMax", feedbackMax);
        m_mat.SetTexture("_PrevTex", pre_tex);
        EnsureKeyword(m_mat, "USE_MOTION_BLUR", useMotionBlur);
        EnsureKeyword(m_mat, "USE_CLOSEST_DEPTH", useClosestDepth);
        EnsureKeyword(m_mat, "USE_ADD_NOISE", useAddNoise);

        RenderTexture internalDestination = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, source.antiAliasing);
        // 这里为什么不能直接拷贝到backbuffer上？？
        Graphics.Blit(source, internalDestination, m_mat, 0);
        Graphics.Blit(internalDestination, destination);
        Graphics.Blit(internalDestination, pre_tex);
        RenderTexture.ReleaseTemporary(internalDestination);
    }

    // common part
    void Clear()
    {
        _camera.ResetProjectionMatrix();
        activeSample = Vector4.zero;
        activeIndex = -2;
    }

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        _camera.depthTextureMode = DepthTextureMode.Depth;

        InitializeHalton_2_3(points_Halton_2_3_x16);
        initMaterial();
    }

    void OnDisable()
    {
        Clear();
    }
}
