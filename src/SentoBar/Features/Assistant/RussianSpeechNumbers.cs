using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SentoBar;

internal static partial class RussianSpeechNumbers
{
    private static readonly string[] Ones="ноль один два три четыре пять шесть семь восемь девять десять одиннадцать двенадцать тринадцать четырнадцать пятнадцать шестнадцать семнадцать восемнадцать девятнадцать".Split();
    private static readonly string[] Tens=["","","двадцать","тридцать","сорок","пятьдесят","шестьдесят","семьдесят","восемьдесят","девяносто"];
    private static readonly string[] Hundreds=["","сто","двести","триста","четыреста","пятьсот","шестьсот","семьсот","восемьсот","девятьсот"];
    private static readonly string[][] Scales=[[],["тысяча","тысячи","тысяч"],["миллион","миллиона","миллионов"],["миллиард","миллиарда","миллиардов"],["триллион","триллиона","триллионов"]];
    private static readonly string[] DecimalPlaces=["","десят","сот","тысячн","десятитысячн","стотысячн","миллионн"];

    [GeneratedRegex(@"(?<sign>(?<![\p{L}\d])[−+-])?(?<int>[0-9]{1,3}(?:[ \u00a0\u202f][0-9]{3}(?![0-9]))+|[0-9]+)(?:(?<sep>[.,/])(?<part>[0-9]+))?(?<percent>[ \u00a0]?%)?")]
    private static partial Regex Number();

    internal static string Speak(string text)
    {
        return Number().Replace(text,m=>
        {
            string digits=string.Concat(m.Groups["int"].Value.Where(char.IsAsciiDigit));
            string part=m.Groups["part"].Value;
            string sign=m.Groups["sign"].Value switch {"−" or "-"=>"минус ","+"=>"плюс ",_=>""};
            bool percent=m.Groups["percent"].Success;
            var following=text.AsSpan(m.Index+m.Length).TrimStart();
            bool feminine=!percent && (following.StartsWith("минут",StringComparison.OrdinalIgnoreCase) || following.StartsWith("секунд",StringComparison.OrdinalIgnoreCase));
            string words=Cardinal(digits,feminine);
            bool decimalPoint=m.Groups["sep"].Value is "." or ",";
            if(decimalPoint)
            {
                if(part.Length<DecimalPlaces.Length)
                {
                    string ending=Singular(part)?"ая":"ых";
                    words=Cardinal(digits,true)+(Singular(digits)?" целая ":" целых ")+Cardinal(part.TrimStart('0').PadLeft(1,'0'),true)+" "+DecimalPlaces[part.Length]+ending;
                }
                else words+=" запятая "+DigitNames(part);
            }
            else if(part.Length>0)
            {
                words=(digits,part) switch {("1","2")=>"одна вторая",("1","3")=>"одна третья",("1","4")=>"одна четвертая",("1","60")=>"одна шестидесятая",_=>Cardinal(digits,false)+" делить на "+Cardinal(part,false)};
            }
            if(percent)words+=" "+(decimalPoint || part.Length>0?"процента":Forms(digits) switch {0=>"процент",1=>"процента",_=>"процентов"});
            return sign+words;
        });
    }

    private static int Forms(string digits)
    {
        int end=int.Parse(digits.AsSpan(Math.Max(0,digits.Length-2)),CultureInfo.InvariantCulture);
        if(end is >=11 and <=14)return 2;
        return (end%10) switch {1=>0,2 or 3 or 4=>1,_=>2};
    }
    private static bool Singular(string digits)=>Forms(digits)==0;
    private static string DigitNames(string digits)=>string.Join(" ",digits.Select(c=>Ones[c-'0']));
    private static string Cardinal(string digits,bool feminine)
    {
        if(digits.Length>15 || digits.Length>1 && digits[0]=='0')return DigitNames(digits);
        if(digits=="0")return Ones[0];
        var words=new List<string>();
        int scale=(digits.Length-1)/3;
        for(int i=0;i<digits.Length;scale--)
        {
            int length=digits.Length-i-scale*3;
            string group=digits.Substring(i,length);i+=length;
            int n=int.Parse(group,CultureInfo.InvariantCulture);
            if(n==0)continue;
            if(n>=100)words.Add(Hundreds[n/100]);
            n%=100;
            if(n>=20){words.Add(Tens[n/10]);n%=10;}
            if(n>0)words.Add((scale==1 || scale==0 && feminine)?n switch {1=>"одна",2=>"две",_=>Ones[n]}:Ones[n]);
            if(scale>0)words.Add(Scales[scale][Forms(group)]);
        }
        return string.Join(" ",words);
    }
}
