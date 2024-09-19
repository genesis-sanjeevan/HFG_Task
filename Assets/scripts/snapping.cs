using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class snapping : MonoBehaviour
{
    // inspector
    public float PointRadius = 0.5f;
    public float SmoothSnappingSpeed = 3f;

    [Space]
    public Transform[] DragObjects;

    [Space]
    public Path[] Paths;

    // public
    public int ActivePathIndex
    {
        get => _activePathIndex;
        set
        {
            if (value < 0 || value >= Paths.Length)
                return;

            _activePathIndex = value;
            AssignDragObjectsToActivePath();
        }
    }

    // private
    private PointInfo _downPoint;
    private float _dragValue = 0;
    private float _dragValueTarget = 0;
    private int _activePathIndex = 0;
    private HashSet<Transform> _allFilledPoints;
    private List<PointInfo> _pointsUnderMouse = new List<PointInfo>();

    private Path ActivePath => Paths[ActivePathIndex];
    private PointInfo[] PointsInfo => ActivePath.PointsInfo;
    private bool IsCircular => ActivePath.IsCircular;

    private bool CanMoveForward => ActivePath.IsCircular || ActivePath.PointsInfo.Last().DragObject == null;
    private bool CanMoveBackward => ActivePath.IsCircular || ActivePath.PointsInfo.First().DragObject == null;


    private enum MoveDirection
    {
        Forward,
        Backward
    }
    private MoveDirection _moveDirection;

    private enum State
    {
        None,
        MouseUp,
        MouseDown,
        WaitingDragDistance,
        DraggingPoints,
        AutoSmoothSnapping
    }
    private State _inputState = State.None;


#if UNITY_EDITOR
    [Header("Debug (editor only)")]
    public bool ShowGizmos = true;
    public bool ShowLines = true;
    public bool ShowRadius = true;
#endif


    private void Awake()
    {
        Init();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow))
            StepForward01(1f);
        if (Input.GetKeyDown(KeyCode.LeftArrow))
            StepBackward01(1f);


        if (Input.GetMouseButtonDown(0))
        {
            _inputState = State.MouseDown;
        }

        if (Input.GetMouseButtonUp(0) && _inputState == State.DraggingPoints)
        {
            _inputState = State.MouseUp;
        }

        if (_inputState == State.None)
        {
            return;
        }


        if (_inputState == State.MouseDown)
        {
            PointsUnderMouse(result: _pointsUnderMouse);

            if (_pointsUnderMouse.Count == 0)
            {
                _inputState = State.None;
                return;
            }

            if (_pointsUnderMouse.Count == 1)
            {
                _downPoint = _pointsUnderMouse[0];

                GetForwardAndBackDotToMouse(_downPoint, out float forwDot, out float backDot);

                _moveDirection = forwDot >= backDot ? MoveDirection.Forward : MoveDirection.Backward;
                ActivePathIndex = Array.IndexOf(Paths, _downPoint.PathOwner);

                _inputState = State.DraggingPoints;
            }
            else
            {
                _inputState = State.WaitingDragDistance;
            }
        }



        if (_inputState == State.WaitingDragDistance)
        {
            // return if the distance point-mouse to small
            if (((Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition) - _pointsUnderMouse[0].Pos).sqrMagnitude < PointRadius * PointRadius)
            {
                return;
            }

            float maxDot = -1;

            foreach (PointInfo point in _pointsUnderMouse)
            {
                GetForwardAndBackDotToMouse(point, out float forwDot, out float backDot);

                if (forwDot >= maxDot)
                {
                    maxDot = forwDot;
                    _moveDirection = MoveDirection.Forward;
                    _downPoint = point;
                }

                if (backDot >= maxDot)
                {
                    maxDot = backDot;
                    _moveDirection = MoveDirection.Backward;
                    _downPoint = point;
                }
            }

            ActivePathIndex = Array.IndexOf(Paths, _downPoint.PathOwner);
            _inputState = State.DraggingPoints;

        }


        if (_inputState == State.DraggingPoints)
        {
            if (_dragValue == 0 || _dragValue == 1f)
            {
                GetForwardAndBackDotToMouse(_downPoint, out float forwDot, out float backDot);

                _moveDirection = forwDot >= backDot ? MoveDirection.Forward : MoveDirection.Backward;
            }

            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

            if (_moveDirection == MoveDirection.Forward && CanMoveForward)
            {
                _dragValue = NearestValueOnSegment(_downPoint.Pos, _downPoint.ForwPoint.Pos, mousePos);
                StepForward01(_dragValue);

                if (_dragValue == 1)
                {
                    _downPoint = _downPoint.ForwPoint;
                }
            }
            else if (_moveDirection == MoveDirection.Backward && CanMoveBackward)
            {
                _dragValue = NearestValueOnSegment(_downPoint.Pos, _downPoint.BackPoint.Pos, mousePos);
                StepBackward01(_dragValue);

                if (_dragValue == 1)
                {
                    _downPoint = _downPoint.BackPoint;
                }
            }
        }


        if (_inputState == State.MouseUp)
        {
            _dragValueTarget = Mathf.Round(_dragValue);
            _inputState = State.AutoSmoothSnapping;
        }


        if (_inputState == State.AutoSmoothSnapping)
        {
            _dragValue = Mathf.MoveTowards(_dragValue, _dragValueTarget, SmoothSnappingSpeed * Time.deltaTime);

            if (_moveDirection == MoveDirection.Forward)
            {
                StepForward01(_dragValue);
            }
            else
            {
                StepBackward01(_dragValue);
            }

            if (_dragValue == 0 || _dragValue == 1)
            {
                if (SolutionIsSuccess())
                {
                    Debug.Log("PUZZLE COMPLETE!!!"); // event?
                }

                _inputState = State.None;
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (ShowGizmos == false || Paths == null)
            return;

        foreach (Path path in Paths)
        {
            if (path.Points?.Length < 2)
                continue;

            Gizmos.color = (Application.isPlaying && path == ActivePath) ? Color.green : Color.gray;

            if (ShowLines)
            {
                for (int i = 0; i < path.Points.Length - 1; i++)
                {
                    Gizmos.DrawLine(path.Points[i].position, path.Points[i + 1].position);
                }

                if (path.IsCircular)
                {
                    Gizmos.DrawLine(path.Points[0].position, path.Points[path.Points.Length - 1].position);
                }
            }

            Gizmos.color = Color.gray;
            if (ShowRadius)
            {
                foreach (Transform point in path.Points)
                {
                    Gizmos.DrawWireSphere(point.position, PointRadius);
                }
            }
            Gizmos.color = Color.white;
        }
    }


    public void StepForward01(float value)
    {
        if (CanMoveForward == false)
            return;

        value = Mathf.Clamp01(value);

        foreach (PointInfo pointInfo in PointsInfo.Where(p => p.DragObject != null))
        {
            pointInfo.DragObject.position = Vector2.Lerp(pointInfo.Pos, pointInfo.ForwPoint.Pos, value);
        }

        if (value == 1f)
        {
            var bufferDragObjectLast = PointsInfo.Last().DragObject;

            for (int i = PointsInfo.Length - 1; i > 0; i--)
            {
                PointsInfo[i].DragObject = PointsInfo[i - 1].DragObject;
                PointsInfo[i - 1].DragObject = null;
            }


            if (IsCircular)
            {
                PointsInfo.First().DragObject = bufferDragObjectLast;
            }
        }
    }
    public void StepBackward01(float value)
    {
        if (CanMoveBackward == false)
            return;

        value = Mathf.Clamp01(value);

        foreach (PointInfo pointInfo in PointsInfo.Where(p => p.DragObject != null))
        {
            pointInfo.DragObject.position = Vector2.Lerp(pointInfo.Pos, pointInfo.BackPoint.Pos, value);
        }

        if (value == 1f)
        {
            var bufferDragObjectFirst = PointsInfo.First().DragObject;

            for (int i = 0; i < PointsInfo.Length - 1; i++)
            {
                PointsInfo[i].DragObject = PointsInfo[i + 1].DragObject;
                PointsInfo[i + 1].DragObject = null;
            }

            if (IsCircular)
            {
                PointsInfo.Last().DragObject = bufferDragObjectFirst;
            }
        }
    }



    private void Init()
    {
        foreach (var path in Paths)
        {
            path.Init();
        }

        if (Paths?.Length != 0)
        {
            SnapDragObjectsPositionToNearestPoints();
            DefineFilledPointsAsSolution();

            ActivePathIndex = 0;
            _inputState = State.None;
        }
    }
    private void DefineFilledPointsAsSolution()
    {
        _allFilledPoints = new HashSet<Transform>();

        foreach (var point in Paths.SelectMany(p => p.Points).Distinct())
        {
            foreach (var dragObject in DragObjects)
            {
                if ((point.position - dragObject.position).sqrMagnitude <= PointRadius * PointRadius)
                {
                    _allFilledPoints.Add(point);
                    break;
                }
            }
        }
    }
    private bool SolutionIsSuccess()
    {
        bool pointIsFilled;

        foreach (var point in _allFilledPoints)
        {
            pointIsFilled = false;

            foreach (var dragObject in DragObjects)
            {
                if ((dragObject.position - point.position).sqrMagnitude <= PointRadius * PointRadius)
                {
                    pointIsFilled = true;
                    break;
                }
            }

            if (pointIsFilled == false)
            {
                return false;
            }
        }

        return true;
    }

    private void GetForwardAndBackDotToMouse(PointInfo _downPoint, out float forwDot, out float backDot)
    {
        Vector2 downPointToMouseDir = DirToMouse(_downPoint.Pos);

        forwDot = IsPointHasForward(_downPoint) ? Vector2.Dot(downPointToMouseDir, _downPoint.ForwDir) : -1;
        backDot = IsPointHasBackward(_downPoint) ? Vector2.Dot(downPointToMouseDir, _downPoint.BackDir) : -1;
    }
    private void PointsUnderMouse(List<PointInfo> result)
    {
        Vector2 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        result.Clear();

        foreach (Path path in Paths)
        {
            foreach (PointInfo point in path.PointsInfo)
            {
                if ((mouse - point.Pos).sqrMagnitude <= PointRadius * PointRadius)
                {
                    result.Add(point);
                    break;
                }
            }
        }
    }
    private void AssignDragObjectsToActivePath()
    {
        // reset all
        foreach (Path p in Paths)
        {
            foreach (PointInfo point in p.PointsInfo)
            {
                point.DragObject = null;
            }
        }

        // define
        for (int i = 0; i < DragObjects.Length; i++)
        {
            for (int j = 0; j < PointsInfo.Length; j++)
            {
                if (((Vector2)DragObjects[i].position - PointsInfo[j].Pos).sqrMagnitude < PointRadius * PointRadius)
                {
                    DragObjects[i].position = PointsInfo[j].Pos;
                    PointsInfo[j].DragObject = DragObjects[i];
                    break;
                }
            }
        }
    }
    private void SnapDragObjectsPositionToNearestPoints()
    {
        var allPoints = Paths.SelectMany(p => p.Points);
        foreach (var dragObject in DragObjects)
        {
            foreach (var point in allPoints)
            {
                if ((dragObject.position - point.position).sqrMagnitude <= PointRadius * PointRadius)
                {
                    dragObject.position = point.position;
                    break;
                }
            }
        }
    }

    private bool IsPointHasForward(PointInfo p) => p.PathOwner.IsCircular || p.Index != p.PathOwner.PointsInfo.Length - 1;
    private bool IsPointHasBackward(PointInfo p) => p.PathOwner.IsCircular || p.Index != 0;

    private Vector2 DirToMouse(Vector2 from)
    {
        return ((Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition) - from).normalized;
    }
    private float NearestValueOnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        float lengthSquared = (b - a).sqrMagnitude;

        if (lengthSquared == 0)
        {
            return 0;
        }

        float t = Vector2.Dot(p - a, b - a) / lengthSquared;
        return Mathf.Clamp01(t);
    }



    [Serializable]
    public class Path
    {
        public Transform[] Points;
        public bool IsCircular;

        [HideInInspector]
        public PointInfo[] PointsInfo;

        public void Init()
        {
            PointsInfo = new PointInfo[Points.Length];

            for (int i = 0; i < Points.Length; i++)
            {
                int iNext = i == Points.Length - 1 ? 0 : i + 1;
                int iPrev = i == 0 ? Points.Length - 1 : i - 1;

                var pInfo = new PointInfo();
                pInfo.Index = i;
                pInfo.PathOwner = this;
                pInfo.Pos = Points[i].position;

                pInfo.ForwDir = (Points[iNext].position - Points[i].position).normalized;
                pInfo.BackDir = (Points[iPrev].position - Points[i].position).normalized;

                PointsInfo[i] = pInfo;
            }

            // ref to neighbors
            for (int i = 0; i < PointsInfo.Length; i++)
            {
                int iNext = i == PointsInfo.Length - 1 ? 0 : i + 1;
                int iPrev = i == 0 ? PointsInfo.Length - 1 : i - 1;

                PointsInfo[i].BackPoint = PointsInfo[iPrev];
                PointsInfo[i].ForwPoint = PointsInfo[iNext];
            }
        }
    }


    public class PointInfo
    {
        public int Index;
        public Path PathOwner;
        public Vector2 Pos;

        public Vector2 ForwDir;
        public Vector2 BackDir;

        public PointInfo ForwPoint;
        public PointInfo BackPoint;

        public Transform DragObject;
    }
}

