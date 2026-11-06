// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// The native boundary (CONTRIBUTING.md, rule 9). Microsoft's code passes MSMQ its properties as an MQPROPS: a count
// and pointers to pinned arrays of property IDs, PROPVARIANTs and statuses (Interop/MessagePropertyVariants.cs), whose
// strings and byte vectors point to pinned buffers in turn. This class is the only store code that reads or writes
// through those pointers, with Marshal, and only here: it turns them into managed values for the store, and writes
// the store's results back the way MSMQ did, into the caller's buffers or, for a VT_NULL property, into memory it
// allocates for the caller to free with MQFreeMemory.
using System.Messaging.Interop;
using System.Runtime.InteropServices;

namespace System.Messaging.Store
{
    internal sealed class PropertyBlock
    {
        internal const short VT_EMPTY = 0;
        internal const short VT_NULL = 1;
        internal const short VT_I2 = 2;
        internal const short VT_I4 = 3;
        internal const short VT_I1 = 16;
        internal const short VT_UI1 = 17;
        internal const short VT_UI2 = 18;
        internal const short VT_UI4 = 19;
        internal const short VT_I8 = 20;
        internal const short VT_UI8 = 21;
        internal const short VT_LPWSTR = 31;
        internal const short VT_CLSID = 72;
        internal const short VT_VECTOR = 0x1000;
        internal const short VT_VECTOR_UI1 = VT_VECTOR | VT_UI1;
        internal const short VT_VECTOR_LPWSTR = VT_VECTOR | VT_LPWSTR;

        private static readonly int VariantSize = Marshal.SizeOf<MQPROPVARIANTS>();

        private readonly IntPtr values;
        private readonly int[] ids;
        private readonly MQPROPVARIANTS[] variants;

        internal PropertyBlock(MessagePropertyVariants.MQPROPS properties)
        {
            int count = properties == null ? 0 : properties.propertyCount;
            ids = new int[count];
            variants = new MQPROPVARIANTS[count];
            if (count == 0)
                return;
            values = properties.propertyValues;
            Marshal.Copy(properties.propertyIdentifiers, ids, 0, count);
            for (int i = 0; i < count; i++)
                variants[i] = Marshal.PtrToStructure<MQPROPVARIANTS>(values + i * VariantSize);
        }

        internal int Count => ids.Length;

        internal int Id(int index) => ids[index];

        internal short Vt(int index) => variants[index].vt;

        internal int Find(int propertyId)
        {
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] == propertyId)
                    return i;
            return -1;
        }

        // Reading: what the caller sends.

        /// <summary>A scalar property's value, by its VT, unsigned types zero-extended.</summary>
        internal long Integer(int index)
        {
            MQPROPVARIANTS v = variants[index];
            switch (v.vt)
            {
                case VT_UI1: return v.bVal;
                case VT_I1: return (sbyte)v.bVal;
                case VT_UI2: return (ushort)v.iVal;
                case VT_I2: return v.iVal;
                case VT_UI4: return (uint)v.lVal;
                case VT_I4: return v.lVal;
                case VT_UI8:
                case VT_I8: return v.hVal;
                default: return 0;
            }
        }

        internal long Integer(int propertyId, long defaultValue)
        {
            int index = Find(propertyId);
            return index < 0 ? defaultValue : Integer(index);
        }

        /// <summary>A VT_VECTOR | VT_UI1 property's bytes (its cElems bytes).</summary>
        internal byte[] Bytes(int index)
        {
            MQPROPVARIANTS v = variants[index];
            byte[] data = new byte[v.caub.cElems];
            if (data.Length > 0)
                Marshal.Copy(v.caub.pElems, data, 0, data.Length);
            return data;
        }

        /// <summary>The first <paramref name="length"/> bytes of a VT_VECTOR | VT_UI1 property.</summary>
        internal byte[] Bytes(int index, long length)
        {
            MQPROPVARIANTS v = variants[index];
            int count = (int)Math.Max(0, Math.Min(length, v.caub.cElems));
            byte[] data = new byte[count];
            if (count > 0)
                Marshal.Copy(v.caub.pElems, data, 0, count);
            return data;
        }

        /// <summary>
        /// A VT_LPWSTR property: <paramref name="length"/> characters, or up to its terminating null when the caller
        /// gave no length (as MSMQ read it).
        /// </summary>
        internal string String(int index, long length)
        {
            IntPtr pointer = variants[index].ptr;
            if (variants[index].vt != VT_LPWSTR || pointer == IntPtr.Zero)
                return null;
            return length >= 0 ? Marshal.PtrToStringUni(pointer, (int)length) : Marshal.PtrToStringUni(pointer);
        }

        /// <summary>A VT_CLSID property's 16 bytes.</summary>
        internal Guid Guid(int index)
        {
            if (variants[index].vt != VT_CLSID || variants[index].ptr == IntPtr.Zero)
                return System.Guid.Empty;
            byte[] data = new byte[16];
            Marshal.Copy(variants[index].ptr, data, 0, 16);
            return new Guid(data);
        }

        /// <summary>16 bytes at a pointer the caller passed outside the block (MQGetMachineProperties' machine ID).</summary>
        internal static Guid GuidAt(IntPtr pointer)
        {
            byte[] data = new byte[16];
            Marshal.Copy(pointer, data, 0, 16);
            return new Guid(data);
        }

        // Writing: what the caller receives.

        /// <summary>A scalar result, in the VT the caller asked for.</summary>
        internal void SetInteger(int index, long value)
        {
            switch (variants[index].vt)
            {
                case VT_UI1:
                case VT_I1:
                    variants[index].bVal = unchecked((byte)value);
                    break;
                case VT_UI2:
                case VT_I2:
                    variants[index].iVal = unchecked((short)value);
                    break;
                case VT_UI4:
                case VT_I4:
                    variants[index].lVal = unchecked((int)value);
                    break;
                case VT_UI8:
                case VT_I8:
                    variants[index].hVal = value;
                    break;
                default:
                    return;
            }
            Store(index);
        }

        /// <summary>The size of a VT_VECTOR | VT_UI1 buffer the caller passed.</summary>
        internal long Capacity(int index) => variants[index].vt == VT_VECTOR_UI1 ? variants[index].caub.cElems : 0;

        /// <summary>Copies bytes into the caller's VT_VECTOR | VT_UI1 buffer; the caller checked the capacity.</summary>
        internal void WriteBytes(int index, byte[] data)
        {
            if (data.Length > 0)
                Marshal.Copy(data, 0, variants[index].caub.pElems, data.Length);
        }

        /// <summary>Copies a string and its terminating null into the caller's VT_LPWSTR buffer (capacity checked).</summary>
        internal void WriteString(int index, string value)
        {
            if (variants[index].vt != VT_LPWSTR || variants[index].ptr == IntPtr.Zero)
                return;
            char[] characters = (value + "\0").ToCharArray();
            Marshal.Copy(characters, 0, variants[index].ptr, characters.Length);
        }

        /// <summary>Copies 16 bytes into the caller's VT_CLSID buffer.</summary>
        internal void WriteGuid(int index, Guid value)
        {
            if (variants[index].vt != VT_CLSID || variants[index].ptr == IntPtr.Zero)
                return;
            Marshal.Copy(value.ToByteArray(), 0, variants[index].ptr, 16);
        }

        /// <summary>A VT_NULL property's string, in memory the caller frees with MQFreeMemory.</summary>
        internal void AllocateString(int index, string value)
        {
            variants[index].vt = VT_LPWSTR;
            variants[index].ptr = Marshal.StringToCoTaskMemUni(value);
            Store(index);
        }

        /// <summary>A VT_NULL property's GUID, in memory the caller frees with MQFreeMemory.</summary>
        internal void AllocateGuid(int index, Guid value)
        {
            IntPtr memory = Marshal.AllocCoTaskMem(16);
            Marshal.Copy(value.ToByteArray(), 0, memory, 16);
            variants[index].vt = VT_CLSID;
            variants[index].ptr = memory;
            Store(index);
        }

        /// <summary>
        /// A VT_NULL property's string vector (CALPWSTR): an array of string pointers, each, and the array, freed by the
        /// caller with MQFreeMemory.
        /// </summary>
        internal void AllocateStrings(int index, string[] strings)
        {
            IntPtr array = Marshal.AllocCoTaskMem(Math.Max(1, strings.Length) * IntPtr.Size);
            for (int i = 0; i < strings.Length; i++)
                Marshal.WriteIntPtr(array, i * IntPtr.Size, Marshal.StringToCoTaskMemUni(strings[i]));
            variants[index].vt = VT_VECTOR_LPWSTR;
            variants[index].caub.cElems = (uint)strings.Length;
            variants[index].caub.pElems = array;
            Store(index);
        }

        internal static void Free(IntPtr memory)
        {
            if (memory != IntPtr.Zero)
                Marshal.FreeCoTaskMem(memory);
        }

        private void Store(int index)
        {
            Marshal.StructureToPtr(variants[index], values + index * VariantSize, false);
        }
    }
}
