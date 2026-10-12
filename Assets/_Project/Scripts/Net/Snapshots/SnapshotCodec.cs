using System;
using UnityEngine;

namespace PowerCutCrew.Net
{
    /// <summary>Header of an encoded snapshot.</summary>
    public struct SnapshotHeader
    {
        public ulong StreamKey;
        public ushort Seq;
        public float HostTime;
        public bool Full;
        public bool HasRotation;
        public int Count;
    }

    /// <summary>
    /// Compact wire format for many-body physics. Positions are 3 x uint16 relative to an origin within +-extent
    /// (about 1 mm at 32 m), rotations are 4 bytes (smallest three, 10 bits each).
    /// Layout: ulong key | ushort seq | float hostTime | byte flags | byte count | bodies.
    /// Plain C#, unit tested.
    /// </summary>
    public static class SnapshotCodec
    {
        public const int HeaderSize = 8 + 2 + 4 + 1 + 1;
        private const byte FlagFull = 1;
        private const byte FlagRotation = 2;
        private const float QuatRange = 0.70710678f;

        public static int EncodedSize(int count, bool rotation) => HeaderSize + count * (6 + (rotation ? 4 : 0));

        public static byte[] Encode(ulong streamKey, ushort seq, float hostTime, bool full, Vector3[] positions, Quaternion[] rotations,
            int count, Vector3 origin, float extent)
        {
            bool hasRot = rotations != null;
            var buf = new byte[EncodedSize(count, hasRot)];
            int o = 0;
            BitConverter.GetBytes(streamKey).CopyTo(buf, o); o += 8;
            BitConverter.GetBytes(seq).CopyTo(buf, o); o += 2;
            BitConverter.GetBytes(hostTime).CopyTo(buf, o); o += 4;
            buf[o++] = (byte)((full ? FlagFull : 0) | (hasRot ? FlagRotation : 0));
            buf[o++] = (byte)count;
            for (int i = 0; i < count; i++)
            {
                WriteU16(buf, ref o, Quantize(positions[i].x - origin.x, extent));
                WriteU16(buf, ref o, Quantize(positions[i].y - origin.y, extent));
                WriteU16(buf, ref o, Quantize(positions[i].z - origin.z, extent));
                if (hasRot) { WriteU32(buf, ref o, PackRotation(rotations[i])); }
            }
            return buf;
        }

        public static bool TryReadHeader(byte[] data, out SnapshotHeader header)
        {
            header = default;
            if (data == null || data.Length < HeaderSize) { return false; }
            int o = 0;
            header.StreamKey = BitConverter.ToUInt64(data, o); o += 8;
            header.Seq = BitConverter.ToUInt16(data, o); o += 2;
            header.HostTime = BitConverter.ToSingle(data, o); o += 4;
            byte flags = data[o++];
            header.Count = data[o];
            header.Full = (flags & FlagFull) != 0;
            header.HasRotation = (flags & FlagRotation) != 0;
            return data.Length >= EncodedSize(header.Count, header.HasRotation);
        }

        /// <summary>Decode the bodies into the arrays (which must hold at least header.Count entries).</summary>
        public static void DecodeBodies(byte[] data, SnapshotHeader header, Vector3[] positions, Quaternion[] rotations, Vector3 origin, float extent)
        {
            int o = HeaderSize;
            for (int i = 0; i < header.Count; i++)
            {
                float x = Dequantize(ReadU16(data, ref o), extent) + origin.x;
                float y = Dequantize(ReadU16(data, ref o), extent) + origin.y;
                float z = Dequantize(ReadU16(data, ref o), extent) + origin.z;
                positions[i] = new Vector3(x, y, z);
                if (header.HasRotation)
                {
                    Quaternion q = UnpackRotation(ReadU32(data, ref o));
                    if (rotations != null) { rotations[i] = q; }
                }
            }
        }

        public static ushort Quantize(float value, float extent)
        {
            return (ushort)Mathf.RoundToInt(Mathf.Clamp01(value / (extent * 2f) + 0.5f) * 65535f);
        }

        public static float Dequantize(ushort q, float extent)
        {
            return (q / 65535f - 0.5f) * extent * 2f;
        }

        public static uint PackRotation(Quaternion q)
        {
            float[] c = { q.x, q.y, q.z, q.w };
            int largest = 0;
            for (int i = 1; i < 4; i++)
            {
                if (Mathf.Abs(c[i]) > Mathf.Abs(c[largest])) { largest = i; }
            }
            float sign = c[largest] < 0f ? -1f : 1f;
            uint packed = (uint)largest;
            int shift = 2;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) { continue; }
                float v = Mathf.Clamp(c[i] * sign, -QuatRange, QuatRange);
                uint bits = (uint)Mathf.RoundToInt((v / QuatRange * 0.5f + 0.5f) * 1023f);
                packed |= bits << shift;
                shift += 10;
            }
            return packed;
        }

        public static Quaternion UnpackRotation(uint packed)
        {
            int largest = (int)(packed & 3u);
            var c = new float[4];
            float sum = 0f;
            int shift = 2;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) { continue; }
                uint bits = (packed >> shift) & 1023u;
                float v = (bits / 1023f - 0.5f) * 2f * QuatRange;
                c[i] = v;
                sum += v * v;
                shift += 10;
            }
            c[largest] = Mathf.Sqrt(Mathf.Max(0f, 1f - sum));
            return new Quaternion(c[0], c[1], c[2], c[3]);
        }

        private static void WriteU16(byte[] b, ref int o, ushort v)
        {
            b[o++] = (byte)(v & 0xFF);
            b[o++] = (byte)(v >> 8);
        }

        private static ushort ReadU16(byte[] b, ref int o)
        {
            ushort v = (ushort)(b[o] | (b[o + 1] << 8));
            o += 2;
            return v;
        }

        private static void WriteU32(byte[] b, ref int o, uint v)
        {
            for (int i = 0; i < 4; i++) { b[o++] = (byte)((v >> (8 * i)) & 0xFF); }
        }

        private static uint ReadU32(byte[] b, ref int o)
        {
            uint v = 0;
            for (int i = 0; i < 4; i++) { v |= (uint)b[o++] << (8 * i); }
            return v;
        }
    }
}
