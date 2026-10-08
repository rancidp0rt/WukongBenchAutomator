using System.Runtime.InteropServices;

namespace WukongBenchAutomator.Interop;

// RaytracingTier: 0 - нет DXR, 10 - DXR 1.0, 11 - DXR 1.1, -1 - не удалось проверить.
internal sealed record GraphicsAdapter(
    string Name,
    uint VendorId,
    ulong DedicatedVideoMemory,
    bool IsSoftware,
    int RaytracingTier)
{
    public bool SupportsDxr => RaytracingTier >= 10;

    public string DxrText => RaytracingTier switch
    {
        < 0 => "не определено",
        0 => "нет",
        _ => $"DXR {RaytracingTier / 10}.{RaytracingTier % 10}",
    };
}

// VRAM берём из DXGI: Win32_VideoController.AdapterRAM 32-битный и врёт для карт больше 4 ГБ.
// COM вызываем напрямую через vtable, чтобы не тянуть пакеты.
internal static unsafe class GraphicsAdapterProbe
{
    private const int D3D_FEATURE_LEVEL_11_0 = 0xB000;
    private const int D3D12_FEATURE_D3D12_OPTIONS5 = 27;
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    // Индексы методов в vtable (IUnknown: 0-2, IDXGIObject: 3-6, ID3D12Object: 3-6)
    private const int VtblRelease = 2;
    private const int VtblFactoryEnumAdapters1 = 12;
    private const int VtblAdapterGetDesc1 = 10;
    private const int VtblDeviceCheckFeatureSupport = 13;

    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IidD3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiAdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D12FeatureDataOptions5
    {
        public int SrvOnlyTiledResourceTier3;
        public int RenderPassesTier;
        public int RaytracingTier;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [DllImport("d3d12.dll")]
    private static extern int D3D12CreateDevice(IntPtr adapter, int minimumFeatureLevel, ref Guid riid, out IntPtr device);

    public static IReadOnlyList<GraphicsAdapter> Enumerate()
    {
        var adapters = new List<GraphicsAdapter>();
        var iid = IidDxgiFactory1;
        if (CreateDXGIFactory1(ref iid, out var factory) < 0 || factory == IntPtr.Zero)
        {
            return adapters;
        }

        try
        {
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(factory)[VtblFactoryEnumAdapters1];
            for (uint index = 0; ; index++)
            {
                IntPtr adapter;
                var hr = enumAdapters1(factory, index, &adapter);
                if (hr == DXGI_ERROR_NOT_FOUND || hr < 0)
                {
                    break;
                }

                try
                {
                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, DxgiAdapterDesc1*, int>)Vtbl(adapter)[VtblAdapterGetDesc1];
                    DxgiAdapterDesc1 desc;
                    if (getDesc1(adapter, &desc) < 0)
                    {
                        continue;
                    }

                    var name = new string(desc.Description).Trim();
                    var software = (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0;
                    var tier = software ? 0 : QueryRaytracingTier(adapter);
                    adapters.Add(new GraphicsAdapter(name, desc.VendorId, desc.DedicatedVideoMemory, software, tier));
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }

        return adapters;
    }

    private static int QueryRaytracingTier(IntPtr adapter)
    {
        IntPtr device;
        try
        {
            var iid = IidD3D12Device;
            if (D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL_11_0, ref iid, out device) < 0 || device == IntPtr.Zero)
            {
                return -1;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return -1;
        }

        try
        {
            var checkFeatureSupport =
                (delegate* unmanaged[Stdcall]<IntPtr, int, void*, uint, int>)Vtbl(device)[VtblDeviceCheckFeatureSupport];
            D3D12FeatureDataOptions5 options = default;
            var hr = checkFeatureSupport(device, D3D12_FEATURE_D3D12_OPTIONS5, &options, (uint)sizeof(D3D12FeatureDataOptions5));
            return hr < 0 ? 0 : options.RaytracingTier;
        }
        finally
        {
            Release(device);
        }
    }

    private static IntPtr* Vtbl(IntPtr comObject) => *(IntPtr**)comObject;

    private static void Release(IntPtr comObject)
    {
        if (comObject != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(comObject)[VtblRelease])(comObject);
        }
    }
}
