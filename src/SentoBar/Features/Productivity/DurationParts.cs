namespace SentoBar;
internal static class DurationParts
{
    internal const int MaximumSeconds=99*3600+59*60+59;
    internal static int StepPart(int seconds,int unit,int step)
    {
        int part=seconds/unit%60;
        return seconds+(Wrap(part,step,59)-part)*unit;
    }
    internal static int Wrap(int value,int steps,int maximum)=>((value+steps)%(maximum+1)+(maximum+1))%(maximum+1);
}
