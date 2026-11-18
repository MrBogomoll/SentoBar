using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;
internal sealed record PowerPlan(Guid Id,string Name,bool Active)
{
    public override string ToString()=>Name+(Active?Localization.Text(" · Active"):"");
}
internal sealed class PowerPlanView : StackPanel
{
    private readonly ComboBox choices=new(){MinWidth=180,Margin=new(0,12,0,8)};
    private readonly TextBlock status=UI.Text("",12,UI.Muted);
    private readonly Button apply,refresh;
    private CancellationTokenSource? lifetime;
    private readonly Action reflow;
    internal PowerPlanView(Action reflow)
    {
        this.reflow=reflow;
        var heading=UI.Heading(Localization.Text("Power plan"),19);
        heading.TextAlignment=TextAlignment.Center;
        Children.Add(heading);
        var description=UI.Text(Localization.Text("Choose from the power plans available on this PC."),12,UI.Muted);
        description.TextAlignment=TextAlignment.Center;
        description.Margin=new(0,4,0,0);
        Children.Add(description);
        UI.Named(choices,Localization.Text("Available Windows power plans"));Children.Add(choices);
        apply=UI.Button(Localization.Text("Apply power plan"),async()=>await Apply());
        refresh=UI.Button(Localization.Text("Refresh plans"),async()=>await Refresh());
        foreach(var button in new[]{apply,refresh})
        {
            button.HorizontalAlignment=HorizontalAlignment.Center;
            button.Margin=new(0,3,0,3);
            Children.Add(button);
        }
        status.TextAlignment=TextAlignment.Center;
        status.Margin=new(0,6,0,0);
        Children.Add(status);
        Loaded+=async(_,_)=>{lifetime=new();await Refresh();};Unloaded+=(_,_)=>{lifetime?.Cancel();};
    }
    internal static PowerPlan[] Parse(string text)
    {
        var plans=new List<PowerPlan>();
        foreach(string line in text.Split('\n'))
        {
            var match=Regex.Match(line,@"([0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})\s+\((.*)\)\s*(\*)?",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
            if(match.Success && Guid.TryParse(match.Groups[1].Value,out var id))plans.Add(new(id,match.Groups[2].Value,match.Groups[3].Success));
        }
        return plans.ToArray();
    }
    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr root,in Guid scheme,IntPtr subgroup,IntPtr setting,[Out] byte[]? name,ref uint size);

    private static string ReadPlanName(PowerPlan plan)
    {
        uint size=0;
        if(PowerReadFriendlyName(IntPtr.Zero,plan.Id,IntPtr.Zero,IntPtr.Zero,null,ref size)!=0 || size is <2 or >65536)return plan.Name;
        var buffer=new byte[size];
        if(PowerReadFriendlyName(IntPtr.Zero,plan.Id,IntPtr.Zero,IntPtr.Zero,buffer,ref size)!=0)return plan.Name;
        string name=Encoding.Unicode.GetString(buffer,0,(int)size).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(name) || name.StartsWith('@')?plan.Name:name;
    }

    private static async Task<string> Run(string argument,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        int codePage=(int)GetOEMCP();
        var encoding=CodePagesEncodingProvider.Instance.GetEncoding(codePage)??Encoding.UTF8;
        using var process=new Process{StartInfo=new(){FileName=System.IO.Path.Combine(Environment.SystemDirectory,"powercfg.exe"),Arguments=argument,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=encoding,StandardErrorEncoding=encoding}};
        process.Start();
        try
        {
            var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);string result=await output,detail=await error;
            if(process.ExitCode!=0)throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)?Localization.Text("Windows could not change this power plan."):detail.Trim());
            return result;
        }
        catch(OperationCanceledException){try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}throw;}
    }
    private void Busy(bool value){apply.IsEnabled=refresh.IsEnabled=choices.IsEnabled=!value;}
    private async Task Refresh()
    {
        if(lifetime==null)return;Busy(true);
        try
        {
            var plans=Parse(await Run("/list",lifetime.Token));if(lifetime.IsCancellationRequested)return;
            for(int i=0;i<plans.Length;i++)plans[i]=plans[i] with{Name=ReadPlanName(plans[i])};
            choices.ItemsSource=plans;choices.SelectedItem=Array.Find(plans,p=>p.Active);if(choices.SelectedIndex<0 && plans.Length>0)choices.SelectedIndex=0;
            status.Text=plans.Length==0?Localization.Text("No selectable plans were returned by Windows."):plans.Length==1?Localization.Text("This PC exposes one power plan. Additional power modes may be managed by Windows or your manufacturer."):Localization.Text("Select a plan, then Apply. Changes affect the whole PC.");
        }
        catch(OperationCanceledException){if(!lifetime.IsCancellationRequested)status.Text=Localization.Text("Windows took too long to respond. Try Refresh.");}
        catch(Exception ex){if(!lifetime.IsCancellationRequested)status.Text=ex.Message;}
        finally{Busy(false);if(IsVisible)reflow();}
    }
    private async Task Apply()
    {
        if(lifetime==null || choices.SelectedItem is not PowerPlan selected)return;Busy(true);
        try{await Run("/setactive "+selected.Id.ToString("D"),lifetime.Token);if(lifetime.IsCancellationRequested)return;await Refresh();status.Text=Localization.Text("Applied · ")+selected.Name;}
        catch(OperationCanceledException){if(!lifetime.IsCancellationRequested)status.Text=Localization.Text("Change timed out. Refresh to check the active plan.");}
        catch(Exception ex){if(!lifetime.IsCancellationRequested)status.Text=Localization.Text("Not applied · ")+ex.Message;}
        finally{Busy(false);if(IsVisible)reflow();}
    }
}
