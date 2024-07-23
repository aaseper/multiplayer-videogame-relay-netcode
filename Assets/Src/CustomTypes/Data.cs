using System;
using Unity.Netcode;
using UnityEngine;

namespace CustomTypes
{
    [Serializable]
    internal struct PosAndRotNetworkData : INetworkSerializable
    {
        [SerializeField] [HideInInspector] private float _x, _y, _z;
        [SerializeField] [HideInInspector] private float _xr, _yr, _zr;

        internal Vector3 Position
        {
            get => new(_x, _y, _z);
            set
            {
                _x = value.x;
                _y = value.y;
                _z = value.z;
            }
        }

        internal Vector3 Rotation
        {
            get => new(_xr, _yr, _zr);
            set
            {
                _xr = value.x;
                _yr = value.y;
                _zr = value.z;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _x);
            serializer.SerializeValue(ref _y);
            serializer.SerializeValue(ref _z);

            serializer.SerializeValue(ref _xr);
            serializer.SerializeValue(ref _yr);
            serializer.SerializeValue(ref _zr);
        }
    }

    [Serializable]
    public class AxleInfo
    {
        [SerializeField] public WheelCollider leftWheel, rightWheel;
        [SerializeField] public bool motor, steering;
    }

    /*
     * TODO: Unity Relay does not share available rooms natively, so this is just a placeholder.
     * If there was a database available, this would be the data that would be stored in it.
     * Through the use of a database, the server could store the room data and share it with clients,
     * making it like a P2P matchmaking service.
     *
    [Serializable]
    public class RoomData
    {
        public string JoinCode { get; private set; }
        public int CurrentPlayers { get; private set; }
        public bool Available { get; private set; }

        public RoomData(string joinCode, int currentPlayers, bool available)
        {
            JoinCode = joinCode;
            CurrentPlayers = currentPlayers;
            Available = available;
        }

        public string Stringify()
        {
            return JsonUtility.ToJson(this);
        }

        public static RoomData Parse(string json)
        {
            return JsonUtility.FromJson<RoomData>(json);
        }
    }
     */
}