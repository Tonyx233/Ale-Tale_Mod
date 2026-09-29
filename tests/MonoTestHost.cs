using System;
using System.IO;
using System.Runtime.InteropServices;

// Run reflection/IL tests under the game's Mono, without starting Unity or the game.
class MonoTestHost
{
    [DllImport("kernel32", CharSet=CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    [DllImport("mono-2.0-bdwgc", CallingConvention=CallingConvention.Cdecl)] static extern void mono_set_assemblies_path(string path);
    [DllImport("mono-2.0-bdwgc", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr mono_jit_init_version(string name,string version);
    [DllImport("mono-2.0-bdwgc", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr mono_domain_assembly_open(IntPtr domain,string file);
    [DllImport("mono-2.0-bdwgc", CallingConvention=CallingConvention.Cdecl)] static extern int mono_jit_exec(IntPtr domain,IntPtr assembly,int argc,IntPtr argv);
    static int Main(string[] args)
    {
        string game=Environment.GetEnvironmentVariable("HORSE_TEST_GAME");
        if(!SetDllDirectory(Path.Combine(game,"MonoBleedingEdge/EmbedRuntime"))) throw new Exception("Mono directory unavailable");
        mono_set_assemblies_path(Path.Combine(game,"Ale and Tale Tavern_Data/Managed") + Path.PathSeparator + Path.Combine(game,"BepInEx/core") + Path.PathSeparator + Environment.CurrentDirectory);
        IntPtr domain=mono_jit_init_version("TonyModTests","v4.0.30319");
        if(domain==IntPtr.Zero) throw new Exception("Mono initialization failed");
        IntPtr assembly=mono_domain_assembly_open(domain,Path.GetFullPath(args[0]));
        if(assembly==IntPtr.Zero) throw new Exception("Test assembly unavailable");
        IntPtr argv=Marshal.AllocHGlobal(IntPtr.Size*args.Length);
        var strings=new IntPtr[args.Length];
        try {
            for(int i=0;i<args.Length;i++) { strings[i]=Marshal.StringToHGlobalAnsi(args[i]); Marshal.WriteIntPtr(argv,i*IntPtr.Size,strings[i]); }
            return mono_jit_exec(domain,assembly,args.Length,argv);
        } finally { foreach(var s in strings) if(s!=IntPtr.Zero) Marshal.FreeHGlobal(s); Marshal.FreeHGlobal(argv); }
    }
}
