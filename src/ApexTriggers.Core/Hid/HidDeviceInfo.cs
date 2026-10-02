using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ApexTriggers.Core.Hid;

/// <summary>One HID top-level collection as Windows exposes it (each collection has its own path).</summary>
public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort VersionNumber,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    string Product)
{
    public override string ToString() =>
        $"{VendorId:X4}:{ProductId:X4} page 0x{UsagePage:X4} usage 0x{Usage:X2} in {InputReportLength} out {OutputReportLength} \"{Product}\"";

    /// <summary>All present HID collections. Collections that refuse even a zero-access open are skipped.</summary>
    public static List<HidDeviceInfo> Enumerate(ushort? vendorId = null)
    {
        var result = new List<HidDeviceInfo>();
        // Device paths carry "VID_xxxx", so most collections are skipped without being opened.
        var vidTag = vendorId is null ? null : $"vid_{vendorId:x4}";
        foreach (var path in InterfacePaths())
        {
            if (vidTag is not null && !path.Contains(vidTag, StringComparison.OrdinalIgnoreCase)) continue;
            var info = TryQuery(path);
            if (info is null) continue;
            if (vendorId is not null && info.VendorId != vendorId) continue;
            result.Add(info);
        }
        return result;
    }

    /// <summary>Device paths of every present HID collection — cheap, opens nothing.</summary>
    public static IEnumerable<string> InterfacePaths()
    {
        HidNative.HidD_GetHidGuid(out var guid);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (HidNative.CM_Get_Device_Interface_List_Size(out var len, ref guid, null,
                    HidNative.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != HidNative.CR_SUCCESS)
                return [];
            var buffer = new char[len];
            // The list can grow between the two calls; retry rather than fail.
            if (HidNative.CM_Get_Device_Interface_List(ref guid, null, buffer, len,
                    HidNative.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != HidNative.CR_SUCCESS)
                continue;
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        return [];
    }

    private static HidDeviceInfo? TryQuery(string path)
    {
        // Zero access rights: enough for attributes and caps, works even when another process
        // (Steam) holds the device, and never disturbs it.
        using var handle = HidNative.CreateFile(path, 0,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero,
            HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var attrs = new HidNative.HIDD_ATTRIBUTES { Size = System.Runtime.InteropServices.Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
        if (!HidNative.HidD_GetAttributes(handle, ref attrs)) return null;

        if (!HidNative.HidD_GetPreparsedData(handle, out var preparsed)) return null;
        HidNative.HIDP_CAPS caps;
        try
        {
            if (HidNative.HidP_GetCaps(preparsed, out caps) != HidNative.HIDP_STATUS_SUCCESS) return null;
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsed);
        }

        return new HidDeviceInfo(path, attrs.VendorID, attrs.ProductID, attrs.VersionNumber,
            caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength,
            ReadProduct(handle));
    }

    private static string ReadProduct(SafeFileHandle handle)
    {
        var buffer = new byte[256];
        if (!HidNative.HidD_GetProductString(handle, buffer, buffer.Length)) return "";
        var text = Encoding.Unicode.GetString(buffer);
        var nul = text.IndexOf('\0');
        return nul >= 0 ? text[..nul] : text;
    }
}
