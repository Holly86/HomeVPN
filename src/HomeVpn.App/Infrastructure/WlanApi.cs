using System.Runtime.InteropServices;
using System.Text;

namespace HomeVpn.Infrastructure;

internal sealed record WifiConnection(Guid InterfaceGuid, string Ssid, bool SecurityEnabled);

internal static class WlanApi
{
    private const uint WlanClientVersionLonghorn = 2;
    private const int WlanIntfOpcodeCurrentConnection = 7;
    private const int WlanInterfaceStateConnected = 1;

    public static IReadOnlyList<WifiConnection> GetConnectedNetworks()
    {
        var results = new List<WifiConnection>();
        if (WlanOpenHandle(WlanClientVersionLonghorn, IntPtr.Zero, out _, out var clientHandle) != 0)
            return results;

        try
        {
            if (WlanEnumInterfaces(clientHandle, IntPtr.Zero, out var listPtr) != 0 || listPtr == IntPtr.Zero)
                return results;

            try
            {
                var count = Marshal.ReadInt32(listPtr, 0);
                var offset = 8;
                var itemSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();

                for (var i = 0; i < count; i++)
                {
                    var itemPtr = IntPtr.Add(listPtr, offset + i * itemSize);
                    var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(itemPtr);
                    if (info.isState != WlanInterfaceStateConnected)
                        continue;

                    var guid = info.InterfaceGuid;
                    var error = WlanQueryInterface(
                        clientHandle,
                        ref guid,
                        WlanIntfOpcodeCurrentConnection,
                        IntPtr.Zero,
                        out _,
                        out var dataPtr,
                        out _);

                    if (error != 0 || dataPtr == IntPtr.Zero)
                        continue;

                    try
                    {
                        var attributes = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(dataPtr);
                        var ssidBytes = attributes.wlanAssociationAttributes.dot11Ssid.ucSSID ?? [];
                        var length = Math.Min((int)attributes.wlanAssociationAttributes.dot11Ssid.uSSIDLength, ssidBytes.Length);
                        var ssid = Encoding.UTF8.GetString(ssidBytes, 0, length);
                        if (!string.IsNullOrWhiteSpace(ssid))
                            results.Add(new WifiConnection(info.InterfaceGuid, ssid, attributes.wlanSecurityAttributes.bSecurityEnabled));
                    }
                    finally
                    {
                        WlanFreeMemory(dataPtr);
                    }
                }
            }
            finally
            {
                WlanFreeMemory(listPtr);
            }
        }
        finally
        {
            WlanCloseHandle(clientHandle, IntPtr.Zero);
        }

        return results;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strInterfaceDescription;

        public int isState;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_CONNECTION_ATTRIBUTES
    {
        public int isState;
        public int wlanConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;

        public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
        public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_ASSOCIATION_ATTRIBUTES
    {
        public DOT11_SSID dot11Ssid;
        public int dot11BssType;
        public DOT11_MAC_ADDRESS dot11Bssid;
        public int dot11PhyType;
        public uint uDot11PhyIndex;
        public uint wlanSignalQuality;
        public uint ulRxRate;
        public uint ulTxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DOT11_SSID
    {
        public uint uSSIDLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ucSSID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DOT11_MAC_ADDRESS
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] ucDot11MacAddress;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_SECURITY_ATTRIBUTES
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool bSecurityEnabled;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bOneXEnabled;

        public int dot11AuthAlgorithm;
        public int dot11CipherAlgorithm;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(
        uint dwClientVersion,
        IntPtr pReserved,
        out uint pdwNegotiatedVersion,
        out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(
        IntPtr hClientHandle,
        ref Guid pInterfaceGuid,
        int opCode,
        IntPtr pReserved,
        out int pdwDataSize,
        out IntPtr ppData,
        out int pWlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);
}
