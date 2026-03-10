using UnityEngine;
using System.Collections.Generic;


[System.Serializable]
public struct ProjectorData
{
    public Matrix4x4 viewMatrix;
    public Matrix4x4 projectionMatrix;
    public Vector3 position;
    public Vector3 forward;
}

[System.Serializable]
public struct DisplaceableVolume
{
    public Plane basePlane;
    public Plane upperPlane;
    public Plane lowerPlane;
    public float upperBound;
    public float lowerBound;

    public DisplaceableVolume(Plane basePlane, float minDisplacement, float maxDisplacement)
    {
        this.basePlane = basePlane;
        this.upperBound = basePlane.distance + maxDisplacement;
        this.lowerBound = basePlane.distance + minDisplacement;

        // 创建上下边界平面（与基平面平行）
        this.upperPlane = new Plane(basePlane.normal, basePlane.normal * upperBound);
        this.lowerPlane = new Plane(basePlane.normal, basePlane.normal * lowerBound);
    }
}

public partial class ProjectedGridController
{

    Matrix4x4 CalculateRangeConversionMatrix(
        Camera renderingCamera,
        ProjectorData projector,
        DisplaceableVolume volume)
    {
     
        Vector4[] cameraFrustumCornersNDC = GetCameraFrustumCornersNDC();


        Matrix4x4 invCameraViewProj = (renderingCamera.projectionMatrix * renderingCamera.worldToCameraMatrix).inverse;
        List<Vector3> worldSpaceIntersections = new List<Vector3>();


        foreach (Vector4 ndcCorner in cameraFrustumCornersNDC)
        {
            Vector4 worldCorner = invCameraViewProj * ndcCorner;
            worldCorner /= worldCorner.w; 

            if (IsPointInDisplaceableVolume(worldCorner, volume))
            {
                worldSpaceIntersections.Add(worldCorner);
            }
        }


        worldSpaceIntersections.AddRange(CalculateFrustumVolumeIntersections(
            renderingCamera, volume));


        if (worldSpaceIntersections.Count == 0)
        {
            return Matrix4x4.zero; 
        }

        //将所有交点投影到基平面上
        List<Vector3> projectedPoints = new List<Vector3>();
        foreach (Vector3 worldPoint in worldSpaceIntersections)
        {
            Vector3 projectedPoint = ProjectPointToBasePlane(worldPoint, volume.basePlane);
            projectedPoints.Add(projectedPoint);
        }


        Matrix4x4 projectorViewProj = projector.projectionMatrix * projector.viewMatrix;
        List<Vector2> projectorSpacePoints = new List<Vector2>();

        foreach (Vector3 worldPoint in projectedPoints)
        {
            Vector4 projectorSpace = projectorViewProj * new Vector4(worldPoint.x,worldPoint.y,worldPoint.z, 1);
            projectorSpace /= projectorSpace.w; 

            projectorSpacePoints.Add(new Vector2(projectorSpace.x, projectorSpace.y));
        }


        Vector4 xyMinMax = CalculateXYRange(projectorSpacePoints);

      
        //xyMinMax = ApplySafetyMargin(xyMinMax);

        return BuildRangeMatrix(xyMinMax);
    }


    Vector4[] GetCameraFrustumCornersNDC()
    {
        return new Vector4[8]
        {
        new Vector4(-1, -1, -1, 1), // 左下近
        new Vector4( 1, -1, -1, 1), // 右下近
        new Vector4(-1,  1, -1, 1), // 左上近
        new Vector4( 1,  1, -1, 1), // 右上近
        new Vector4(-1, -1,  1, 1), // 左下远
        new Vector4( 1, -1,  1, 1), // 右下远
        new Vector4(-1,  1,  1, 1), // 左上远
        new Vector4( 1,  1,  1, 1)  // 右上远
        };
    }

    bool IsPointInDisplaceableVolume(Vector4 worldPoint, DisplaceableVolume volume)
    {
        float height = worldPoint.y;
        return height >= volume.lowerBound && height <= volume.upperBound;
    }

    List<Vector3> CalculateFrustumVolumeIntersections(Camera camera, DisplaceableVolume volume)
    {
        List<Vector3> intersections = new List<Vector3>();

        Vector3[] frustumCornersWorld = GetCameraFrustumCornersWorld(camera);
        Vector3[][] frustumEdges = GetFrustumEdges(frustumCornersWorld);

        for (int i = 0; i < frustumEdges.Length;i++) 
        {
            Vector3 start = frustumEdges[i][0];
            Vector3 end = frustumEdges[i][1];
            Vector3 upperIntersection;
            if (LinePlaneIntersection(start, end, volume.upperPlane, out upperIntersection))
            {
                if (IsPointOnLineSegment(start, end, upperIntersection) &&
                    IsPointInDisplaceableVolumeHorizontal(upperIntersection, volume))
                {
                    intersections.Add(upperIntersection);
                }
            }

            Vector3 lowerIntersection;
            if (LinePlaneIntersection(start, end, volume.lowerPlane, out lowerIntersection))
            {
                if (IsPointOnLineSegment(start, end, lowerIntersection) &&
                    IsPointInDisplaceableVolumeHorizontal(lowerIntersection, volume))
                {
                    intersections.Add(lowerIntersection);
                }
            }
        }

        return intersections;
    }

    Vector3[] GetCameraFrustumCornersWorld(Camera camera)
    {
        Vector3[] corners1 = new Vector3[4];
        camera.CalculateFrustumCorners(
            new Rect(0, 0, 1, 1),
            camera.nearClipPlane,
            Camera.MonoOrStereoscopicEye.Mono,
            corners1);

        Vector3[] corners2 = new Vector3[4];
        camera.CalculateFrustumCorners(
            new Rect(0, 0, 1, 1),
            camera.farClipPlane,
            Camera.MonoOrStereoscopicEye.Mono,
            corners2);

        List<Vector3> tmpVecLst = new List<Vector3>();
        tmpVecLst.AddRange(corners1);
        tmpVecLst.AddRange(corners2);
    
        // 变换到世界空间
        for (int i = 0; i < tmpVecLst.Count; i++)
        {
            tmpVecLst[i] = camera.transform.TransformPoint(tmpVecLst[i]);
        }

        return tmpVecLst.ToArray();
    }

    Vector3[][] GetFrustumEdges(Vector3[] corners)
    {

        //顺时针
        // 视锥体的12条边
        return new Vector3[12][]
        {
        new Vector3[] { corners[0], corners[1] }, // 近平面底边
        new Vector3[] { corners[1], corners[2] }, // 近平面右边
        new Vector3[] { corners[2], corners[3] }, // 近平面顶边
        new Vector3[] { corners[3], corners[0] }, // 近平面左边
        
        new Vector3[] { corners[4], corners[5] }, // 远平面底边
        new Vector3[] { corners[5], corners[6] }, // 远平面右边
        new Vector3[] { corners[6], corners[7] }, // 远平面顶边
        new Vector3[] { corners[7], corners[4] }, // 远平面左边
        
        new Vector3[] { corners[0], corners[4] }, // 左下棱
        new Vector3[] { corners[1], corners[5] }, // 右下棱
        new Vector3[] { corners[2], corners[6] }, // 左上棱
        new Vector3[] { corners[3], corners[7] }  // 右上棱
        };
    }

    bool LinePlaneIntersection(Vector3 lineStart, Vector3 lineEnd, Plane plane, out Vector3 intersection)
    {
        Ray ray = new Ray(lineStart, (lineEnd - lineStart).normalized);
        float enter;

        if (plane.Raycast(ray, out enter))
        {
            float lineLength = Vector3.Distance(lineStart, lineEnd);
            if (enter <= lineLength)
            {
                intersection = ray.GetPoint(enter);
                return true;
            }
        }

        intersection = Vector3.zero;
        return false;
    }

    bool IsPointOnLineSegment(Vector3 start, Vector3 end, Vector3 point)
    {
        float lineLength = Vector3.Distance(start, end);
        float distToStart = Vector3.Distance(start, point);
        float distToEnd = Vector3.Distance(end, point);

        return Mathf.Abs(distToStart + distToEnd - lineLength) < 0.001f;
    }

    bool IsPointInDisplaceableVolumeHorizontal(Vector3 point, DisplaceableVolume volume)
    {
        // 检查点在水平方向是否在位移体积的边界内
        // tod do (fix me) 这里简化处理，实际应该根据位移体积的水平边界检查
        return true; // 先假设无限大水平范围
    }

    Vector3 ProjectPointToBasePlane(Vector3 worldPoint, Plane basePlane)
    {
        float distance = basePlane.GetDistanceToPoint(worldPoint);
        Vector3 projected = worldPoint - distance * basePlane.normal;
        return projected;
    }

    Vector4 CalculateXYRange(List<Vector2> points)
    {
        if (points.Count == 0)
            return new Vector2(-1, 1); // 默认范围

        float xMin = float.MaxValue;
        float xMax = float.MinValue;
        float yMin = float.MaxValue;
        float yMax = float.MinValue;

        foreach (Vector2 point in points)
        {
            xMin = Mathf.Min(xMin, point.x);
            xMax = Mathf.Max(xMax, point.x);
            yMin = Mathf.Min(yMin, point.y);
            yMax = Mathf.Max(yMax, point.y);
        }

        return new Vector4(xMin, xMax, yMin, yMax);
    }

    Vector2 ApplySafetyMargin(Vector4 xyMinMax)
    {
        float xMin = xyMinMax.x;
        float xMax = xyMinMax.y;
        float yMin = xyMinMax.z;
        float yMax = xyMinMax.w;
        float xMargin = (xMax - xMin) * 0.05f;
        float yMargin = (yMax - yMin) * 0.05f;

        xMin -= xMargin;
        xMax += xMargin;
        yMin -= yMargin;
        yMax += yMargin;

        xMin = Mathf.Max(xMin, -2.0f);
        xMax = Mathf.Min(xMax, 2.0f);
        yMin = Mathf.Max(yMin, -2.0f);
        yMax = Mathf.Min(yMax, 2.0f);

        if (xMin >= xMax) xMax = xMin + 0.1f;
        if (yMin >= yMax) yMax = yMin + 0.1f;

        return new Vector4(xMin, xMax, yMin, yMax);
    }

    Matrix4x4 BuildRangeMatrix(Vector4 xyMinMax)
    {
        float xMin = xyMinMax.x;
        float xMax = xyMinMax.y;
        float yMin = xyMinMax.z;
        float yMax = xyMinMax.w;

        // 论文中的范围矩阵：
        // [ (xMax-xMin)   0       0   xMin ]
        // [    0      (yMax-yMin) 0   yMin ]
        // [    0          0       1     0  ]
        // [    0          0       0     1  ]

        Matrix4x4 rangeMatrix = Matrix4x4.identity;
        rangeMatrix.m00 = xMax - xMin;  // 缩放x
        rangeMatrix.m03 = xMin;         // 平移x
        rangeMatrix.m11 = yMax - yMin;  // 缩放y
        rangeMatrix.m13 = yMin;         // 平移y

        return rangeMatrix;
    }
}