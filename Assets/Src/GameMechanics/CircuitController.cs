using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public class CircuitController : MonoBehaviour
{
    [SerializeField] public List<GameObject> circuits;
    
    private List<float[]> _cumArcLength;
    private List<Vector3[]> _pathPos;

    [SerializeField] [HideInInspector] private List<LineRenderer> _circuitsPath = new();
    [SerializeField] [HideInInspector] private List<float> _circuitsLength = new();
    [SerializeField] [HideInInspector] private int _currentCircuit;
    
    public int NumCircuits { get; private set; }

    public float CircuitLength => _circuitsLength[_currentCircuit];

    private void Start()
    {
        NumCircuits = circuits.Count;
        _cumArcLength = Enumerable.Repeat<float[]>(null, NumCircuits).ToList();
        _pathPos = Enumerable.Repeat<Vector3[]>(null, NumCircuits).ToList();
        
        SetDefaultCircuit();

        for (var i = 0; i < NumCircuits; i++)
        {
            _circuitsPath.Add(circuits[i].GetComponent<LineRenderer>());

            var numPoints = _circuitsPath[i].positionCount;
            _pathPos[i] = new Vector3[numPoints];
            _cumArcLength[i] = new float[numPoints];
            _circuitsPath[i].GetPositions(_pathPos[i]);

            // Compute circuit arc-length
            _cumArcLength[_currentCircuit][0] = 0;

            for (var j = 1; j < _pathPos[i].Length; ++j)
            {
                var length = (_pathPos[i][j] - _pathPos[i][j - 1]).magnitude;
                _cumArcLength[i][j] = _cumArcLength[i][j - 1] + length;
            }

            _circuitsLength.Add(_cumArcLength[i][_cumArcLength[i].Length - 1]);
        }
    }

    // Calculate the car position projected in the line renderer.
    public float ComputeClosestPointArcLength(Vector3 posIn, out int segIdx, out Vector3 posProjOut, out float distOut)
    {
        var minSegIdx = 0;
        var minArcL = float.NegativeInfinity;
        var minDist = float.PositiveInfinity;
        var minProj = Vector3.zero;

        // Check segments for valid projections of the point
        for (var i = 0; i < _pathPos[_currentCircuit].Length - 1; ++i)
        {
            var pathVec = (_pathPos[_currentCircuit][i + 1] - _pathPos[_currentCircuit][i]).normalized;
            var segLength = (_pathPos[_currentCircuit][i + 1] - _pathPos[_currentCircuit][i]).magnitude;


            var carVec = posIn - _pathPos[_currentCircuit][i];
            var dotProd = Vector3.Dot(carVec, pathVec);

            if (dotProd < 0)
                continue;

            if (dotProd > segLength)
                continue; // Passed

            var proj = _pathPos[_currentCircuit][i] + dotProd * pathVec;
            var dist = (posIn - proj).magnitude;
            if (dist < minDist)
            {
                minDist = dist;
                minProj = proj;
                minSegIdx = i;
                minArcL = _cumArcLength[_currentCircuit][i] + dotProd;
            }
        }

        // If there was no valid projection check nodes
        if (float.IsPositiveInfinity(minDist)) //minDist == float.PositiveInfinity
            for (var i = 0; i < _pathPos[_currentCircuit].Length - 1; ++i)
            {
                var dist = (posIn - _pathPos[_currentCircuit][i]).magnitude;
                if (dist < minDist)
                {
                    minDist = dist;
                    minSegIdx = i;
                    minProj = _pathPos[_currentCircuit][i];
                    minArcL = _cumArcLength[_currentCircuit][i];
                }
            }

        segIdx = minSegIdx;
        posProjOut = minProj;
        distOut = minDist;

        return minArcL;
    }

    public void SetNextCircuit(bool hostLeft = default)
    {
        if (hostLeft)
        {
            // Set default circuit
            _currentCircuit = 0;
            SetDefaultCircuit();
        }
        else
        {
            circuits[_currentCircuit].SetActive(false);
            _currentCircuit = (_currentCircuit + 1) % NumCircuits;
            circuits[_currentCircuit].SetActive(true);
        }
    }
    
    private void SetDefaultCircuit()
    {
        circuits.ForEach(circuit => circuit.SetActive(false));
        circuits[_currentCircuit].SetActive(true);
    }

    public Vector3[] GetRandomCircuitProjPos(int num)
    {
        var positions = new Vector3[10];

        for (var i = 0; i < num; i++)
        {
            var length = _pathPos[_currentCircuit].Length - 2;

            var randomDot = Mathf.FloorToInt(UnityEngine.Random.Range(0, length));

            var pathVec = (_pathPos[_currentCircuit][randomDot + 1] - _pathPos[_currentCircuit][randomDot]).normalized;
            var segLength = (_pathPos[_currentCircuit][randomDot + 1] - _pathPos[_currentCircuit][randomDot]).magnitude;

            var pos = _pathPos[_currentCircuit][randomDot] + pathVec * UnityEngine.Random.Range(0, segLength);

            positions[i] = pos;
        }

        return positions;
    }
}