using System.Runtime.InteropServices;

namespace WeChatFerry;

public static partial class WcfSdk
{
    private const string LibWcfSdk = "wcf_sdk.dll";
    private const string LibRelativePath = @"runtimes\win-x64\native\";
    
    static WcfSdk()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(WcfSdk).Assembly, 
            (libraryName, _, _) => NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, LibRelativePath, libraryName)));
    }

    [LibraryImport(LibWcfSdk, EntryPoint = "WxInitSDK")]
    private static partial int WxInitSdk([MarshalAs(UnmanagedType.Bool)] bool debug = false, int port = 10086);
    
    [LibraryImport(LibWcfSdk, EntryPoint = "WxDestroySDK")]
    private static partial int WxDestroySdk();
    
    public static void Initialize(bool debug = false, int port = 10086)
    {
        var status = WxInitSdk(debug, port);
        if (status != 0)
        {
            throw new ApplicationException($"Failed to initialize WxSdk: {status}");
        }
    }
    
    public static void Destroy()
    {
        var status = WxDestroySdk();
        if (status is not 0 and not -1)  // -1: not initialized
        {
            throw new ApplicationException($"Failed to destroy WxSdk: {status}");
        }
    }
}