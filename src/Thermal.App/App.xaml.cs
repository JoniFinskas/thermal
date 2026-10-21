using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Thermal.Core;
namespace Thermal.App;
public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        if(e.Args.SequenceEqual(new[]{"--describe"})) {
            // Offline apphost/runtime/admission inspection: no window, inventory or sampler is constructed.
            Console.WriteLine(JsonSerializer.Serialize(new{Kind="ThermalDriverFreeDevelopment",AppVersion=AppVersion.Current,RuntimeVersion=Environment.Version.ToString(),RuntimeDirectory=System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),CpuDriverAccessAllowed=false,ServiceActivationAllowed=false,AcerLiveAllowed=AcerAdmission.LiveAllowed,LocalAcerReaderEnabled=LocalAcerReaderHost.Enabled,AcerAutomaticRequest=BuildPolicy.RequestAcerOnOpen,PublicBuild=BuildPolicy.PublicBuild,PublicReleaseQualified=false}));
            Shutdown(0);return;
        }
        base.OnStartup(e);
        var window=new MainWindow();MainWindow=window;window.Show();
    }
}
