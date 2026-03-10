using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public partial class ProjectedGridController : MonoBehaviour
{
    [Header("Grid Settings")]
    public Material gridMaterial;
    public float gridScale = 100f;
    public float basePlaneHeight = 0f;
    
    [Header("Projector Settings")]
    public float projectorElevation = 10f;
    public Vector2Int gridResolution = new Vector2Int(64, 64);
    public Vector2 displacementRange = new Vector2(-1f, 1f);
    
    [Header("Visual Settings")]
    public Color gridColor = Color.white;
    public float gridSize = 1f;
    public float gridThickness = 0.02f;
    public float fadeStart = 50f;
    public float fadeEnd = 100f;
    
    private Mesh gridMesh;
    private Camera mainCamera;
    
    void OnEnable()
    {
        CreateGridMesh();
        mainCamera = Camera.main;
        
        if (gridMaterial == null)
        {
            gridMaterial = new Material(Shader.Find("Custom/InfiniteProjectedGrid"));
        }
    }
    
    void OnDisable()
    {
        if (gridMesh != null)
        {
            if (Application.isPlaying)
                Destroy(gridMesh);
            else
                DestroyImmediate(gridMesh);
        }
    }
    
    void Update()
    {
        if (mainCamera == null || gridMaterial == null || gridMesh == null)
            return;
            
        UpdateProjectorSystem();
        UpdateMaterialProperties();

        Bounds bounds = new Bounds(mainCamera.transform.position, Vector3.one * 90000f);
        gridMesh.bounds = bounds;
        RenderGrid();
    }

    void CreateGridMesh()
    {
        gridMesh = new Mesh();
        gridMesh.name = "ProjectedGrid";

        // 创建高分辨率的单位平面网格
        int resolution = gridResolution.x;
        Vector3[] vertices = new Vector3[resolution * resolution];
        Vector2[] uv = new Vector2[resolution * resolution];
        int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int index = z * resolution + x;
                float u = (float)x / (resolution - 1);
                float v = (float)z / (resolution - 1);

                vertices[index] = new Vector3(u, 0, v);
                uv[index] = new Vector2(u, v);
            }
        }

        int triIndex = 0;
        for (int z = 0; z < resolution - 1; z++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int bottomLeft = z * resolution + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = (z + 1) * resolution + x;
                int topRight = topLeft + 1;

                triangles[triIndex++] = bottomLeft;
                triangles[triIndex++] = topLeft;
                triangles[triIndex++] = bottomRight;

                triangles[triIndex++] = bottomRight;
                triangles[triIndex++] = topLeft;
                triangles[triIndex++] = topRight;
            }
        }

        gridMesh.vertices = vertices;
        gridMesh.uv = uv;
        gridMesh.triangles = triangles;
        gridMesh.RecalculateNormals();
    }

    Vector4 projectorPosDebug;
    Vector4 projectorLookAtDebug;
    void UpdateProjectorSystem()
    {
        if (mainCamera == null) return;
        
        DisplaceableVolume volume = new DisplaceableVolume(
            new Plane(Vector3.up, new Vector3(0, basePlaneHeight, 0)),
            displacementRange.x, displacementRange.y);

        ProjectorData projector = CreateProjectorData(volume);

        Matrix4x4 rangeConversionMatrix = CalculateRangeConversionMatrix(mainCamera, projector, volume);


        //Matrix4x4 rangeConversionMatrix = CalculateRangeConversionMatrix(viewProjectionMatrix);

        // 最终投影仪矩阵：M_projector = M_range · [M_view · M_perspective]^(-1)

        Matrix4x4 viewProj = projector.projectionMatrix * projector.viewMatrix;
        Matrix4x4 invViewProj = viewProj.inverse;

        Matrix4x4 projectorMatrix = invViewProj * rangeConversionMatrix;

//        Debug.LogError($"rangeConversionMatrix => \n {rangeConversionMatrix}");

        gridMaterial.SetMatrix("_ProjectorMatrix", projectorMatrix);
        gridMaterial.SetMatrix("_RangeConversionMatrix", rangeConversionMatrix);
    }
    

     ProjectorData CreateProjectorData(DisplaceableVolume volume)
    {
        ProjectorData projector = new ProjectorData();
        
        Vector3 projectorPos = CalculateProjectorPosition(mainCamera.transform.position);
        Vector3 lookAt = CalculateProjectorLookAt(mainCamera.transform.position, 
                                                 mainCamera.transform.forward, 
                                                 projectorPos,volume);


        projector.viewMatrix = Matrix4x4.LookAt(projectorPos, lookAt, Vector3.up).inverse;


        projector.projectionMatrix = mainCamera.projectionMatrix;

        projector.position = projectorPos;
        projector.forward = (lookAt - projectorPos).normalized;
        
        return projector;
    }

    
    Vector3 CalculateProjectorPosition(Vector3 cameraPos)
    {
        Vector3 projectorPos = cameraPos;
        
        // 确保投影仪在基平面上方
        if (projectorPos.y < basePlaneHeight + projectorElevation)
        {
            projectorPos.y = basePlaneHeight + projectorElevation;
        }

        projectorPos.y += projectorElevation;
        
        return projectorPos;
    }
    
    Vector3 CalculateProjectorLookAt(Vector3 cameraPos, Vector3 cameraForward, Vector3 projectorPos,DisplaceableVolume volume)
    {
        Vector3 method1LookAt = Vector3.zero;
        if (cameraForward.y < -0.01f) // 确保不平行于基平面
        {
            float t = (basePlaneHeight - cameraPos.y) / cameraForward.y;
            method1LookAt = cameraPos + cameraForward * t;
        }
        else
        {
            method1LookAt = cameraPos + cameraForward * 100f; // 远处点
        }

       // LinePlaneIntersection(cameraPos, cameraPos + cameraForward*80, volume.basePlane, out method1LookAt);

        Vector3 method2LookAt = cameraPos + cameraForward * 50f;
        method2LookAt.y = basePlaneHeight;

        float cameraPitch = Mathf.Asin(cameraForward.y) * Mathf.Rad2Deg;
        float blendFactor = Mathf.Clamp01((cameraPitch + 10f) / 45f);
        
        Vector3 finalLookAt = Vector3.Lerp(method2LookAt, method1LookAt, blendFactor);
        
        return finalLookAt;
    }
    
    Matrix4x4 CalculateRangeConversionMatrix(Matrix4x4 viewProjectionMatrix)
    {
        float xMin = -1f;
        float xMax = 1f;
        float yMin = -1f;
        float yMax = 1f;
        
        Matrix4x4 rangeMatrix = Matrix4x4.identity;
        rangeMatrix.m00 = xMax - xMin;
        rangeMatrix.m03 = xMin;
        rangeMatrix.m11 = yMax - yMin;
        rangeMatrix.m13 = yMin;
        
        return rangeMatrix;
    }
    
    void UpdateMaterialProperties()
    {
        if (mainCamera == null || gridMaterial == null) return;
        
        gridMaterial.SetColor("_GridColor", gridColor);
        gridMaterial.SetFloat("_GridSize", gridSize);
        gridMaterial.SetFloat("_GridThickness", gridThickness);
        gridMaterial.SetFloat("_FadeStart", fadeStart);
        gridMaterial.SetFloat("_FadeEnd", fadeEnd);
        gridMaterial.SetFloat("_BasePlaneHeight", basePlaneHeight);
        gridMaterial.SetFloat("_GridScale", gridScale);
        gridMaterial.SetFloat("_ProjectorElevation", projectorElevation);
        gridMaterial.SetVector("_GridResolution", (Vector2)gridResolution);
        gridMaterial.SetVector("_DisplacementRange", displacementRange);
        gridMaterial.SetVector("_CameraPositionWS", mainCamera.transform.position);
    }
    
    void RenderGrid()
    {
        if (gridMesh == null || gridMaterial == null) return;
        
        Graphics.DrawMesh(gridMesh, Matrix4x4.identity, gridMaterial, 
            gameObject.layer, null, 0, null, 
            ShadowCastingMode.Off, false);
    }
    
    void OnValidate()
    {
        if (gridMaterial != null)
        {
            UpdateMaterialProperties();
        }
    }
    
    #if UNITY_EDITOR
    void OnDrawGizmos()
    {

        if (!Application.isPlaying && mainCamera != null)
        {
            UpdateProjectorSystem();
            UpdateMaterialProperties();
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(projectorPosDebug, 0.2f);
            Gizmos.DrawLine(projectorPosDebug, projectorLookAtDebug);
        }
    }
    #endif
}