using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Ude;

namespace EncodingChecker
{
    public partial class PreviewForm : Form
    {
        private Dictionary<string, string> _encodingNameMap = new Dictionary<string, string> {
            {"UTF-8","utf-8" },
            {"UTF-16LE","utf-16" },
            {"UTF-16BE","utf-16" },
            {"UTF-32BE","utf-32BE" },
            {"UTF-32LE","utf-32" },
            //{"X-ISO-10646-UCS-4-3412","" },
            //{"X-ISO-10646-UCS-4-2413","" },
            //{"windows-1251","" },
            //{"windows-1252","" },
            //{"windows-1253","" },
            //{"windows-1255","" },
            {"Big-5","big5" },
            {"EUC-KR","euc-kr" },
            {"EUC-JP","edu-jp" },
            //{"EUC-TW","" },
            { "gb18030","GB18030" },
            //{"ISO-2022-JP","" },
            //{"ISO-2022-CN","" },
            //{"ISO-2022-KR","" },
            {"HZ-GB-2312","hz-gb-2312" },
            //{"Shift-JIS","" },
            //{"x-mac-cyrillic","" },
            //{"KOI8-R","" },
            {"IBM855","IBM855" },
            //{"IBM866","" },
            //{"ISO-8859-2","" },
            //{"ISO-8859-5","" },
            //{"ISO-8859-7","" },
            //{"ISO-8859-8","" },
            //{"TIS620","" }
        };
        private string content;
        private string charset;
        private string targetCharset;
        private Translate translate;
        private string filePath;

        public PreviewForm(string content, string charset,string targetCharset, Translate translate, string filePath)
        {
            InitializeComponent();

            this.content = content;
            this.charset = charset;
            this.targetCharset = targetCharset;
            this.translate = translate;
            this.filePath = filePath;
        }

        private void PreviewForm_Load(object sender, EventArgs e)
        {
            textBox1.Text = content;
            label1.Text = charset;


            var translateTypes = Enum.GetValues(typeof(Translate));
            foreach(Translate translateType in translateTypes)
            {
                comboBoxTranslate.Items.Add(translateType.ToString());
            }
            comboBoxTranslate.Text = translate.ToString();
            //comboBoxTranslate.SelectedIndex = 0;

            IEnumerable<string> validCharsets = GetSupportedCharsets();
            foreach(string validCharset in validCharsets)
            {
                lstConvert.Items.Add(validCharset);
            }
            lstConvert.Text = targetCharset;
            //if(lstConvert.Items.Count > 0)
            //    lstConvert.SelectedIndex = 0;
        }


        private void buttonSave_Click(object sender, EventArgs e)
        {
            if(string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show("unknow filePath");
                return;
            }

            string targetCharset = (string)lstConvert.SelectedItem;
            var encodingName = _encodingNameMap[targetCharset];

            using(StreamWriter writer = new StreamWriter(filePath, false, Encoding.GetEncoding(encodingName)))
            {
                writer.Write(textBox2.Text);
                writer.Flush();
                MessageBox.Show("Convert successed.");
            }
        }


        private static IEnumerable<string> GetSupportedCharsets()
        {
            //Using reflection, figure out all the charsets that the Ude framework supports by reflecting
            //over all the strings constants in the Ude.Charsets class. These represent all the encodings
            //that can be detected by the program.
            FieldInfo[] charsetConstants = typeof(Charsets).GetFields(BindingFlags.GetField | BindingFlags.Static | BindingFlags.Public);
            foreach(FieldInfo charsetConstant in charsetConstants)
            {
                if(charsetConstant.FieldType == typeof(string))
                    yield return (string)charsetConstant.GetValue(null);
            }
        }

        private void comboBoxTranslate_SelectedIndexChanged(object sender, EventArgs e)
        {
            var translate = (Translate)Enum.Parse(typeof(Translate), comboBoxTranslate.Text);
             textBox2.Text = Common.TranslateContent(textBox1.Text, translate);
        }
    }
}
