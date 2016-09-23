using System;
using System.Collections.Generic;
using System.Text;

namespace EncodingChecker
{
    static class Common
    {
        internal static string TranslateContent(string content, Translate translate)
        {
            if(translate == Translate.ToSimplified)
                return ZhConvert.ToSimplified(content);
            else if(translate == Translate.ToTraditional)
                return ZhConvert.ToTraditional(content);
            else
                return content;
        }

    }
}
