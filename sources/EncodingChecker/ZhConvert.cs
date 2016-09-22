using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace EncodingChecker
{
    static class ZhConvert
    {
        ///
        /// 使用系統 kernel32.dll 進行轉換
        ///
        private const int LocaleSystemDefault = 0x0800;
        private const int LcmapSimplifiedChinese = 0x02000000;
        private const int LcmapTraditionalChinese = 0x04000000;
  
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int LCMapString(int locale, int dwMapFlags, string lpSrcStr, int cchSrc, [Out] string lpDestStr, int cchDest);
  
        /// <summary>
        ///  ToSimplified("她來聽我　的演唱會　在十七歲的初戀　第一次約會，繁轉簡");
        /// </summary>
        /// <param name="argSource"></param>
        /// <returns></returns>
        public static string ToSimplified(string argSource)
        {
            var t = new String(' ', argSource.Length);
            LCMapString(LocaleSystemDefault, LcmapSimplifiedChinese, argSource, argSource.Length, t, argSource.Length);
            return t;
        }
  
        /// <summary>
        /// ToTraditional("她来听我　的演唱会　在十七岁的初恋　第一次约会，簡轉繁");
        /// </summary>
        /// <param name="argSource"></param>
        /// <returns></returns>
        public static string ToTraditional(string argSource)
        {
            var t = new String(' ', argSource.Length);
            LCMapString(LocaleSystemDefault, LcmapTraditionalChinese, argSource, argSource.Length, t, argSource.Length);
            return t;
        }
  

        ///
        /// 使用 Office Word (Microsoft.Office.Interop.Word) 進行轉換
        ///
        //public static string ConvertUsingWord(string argSource, bool argIsCht)
        //{
        //    var doc = new Document();
        //    doc.Content.Text = argSource;
        //    doc.Content.TCSCConverter(
        //        argIsCht
        //            ? WdTCSCConverterDirection.wdTCSCConverterDirectionTCSC
        //            : WdTCSCConverterDirection.wdTCSCConverterDirectionSCTC, true, true);
        //    var ret = doc.Content.Text;
        //    object saveChanges = false;
        //    object originalFormat = Missing.Value;
        //    object routeDocument = Missing.Value;
        //    doc.Close(ref saveChanges, ref originalFormat, ref routeDocument);
        //    return ret;
        //}
    }
}
