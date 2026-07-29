using System.Text;

namespace AzertyCommander;

internal sealed class TextViewerForm : Form
{
    private readonly TextBox _textBox = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    public TextViewerForm(string path)
    {
        Text = "Просмотр - " + Path.GetFileName(path);
        StartPosition = FormStartPosition.CenterParent;
        Width = 920;
        Height = 680;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        _textBox.Dock = DockStyle.Fill;
        _textBox.Multiline = true;
        _textBox.ReadOnly = true;
        _textBox.ScrollBars = ScrollBars.Both;
        _textBox.WordWrap = false;
        _textBox.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);

        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(_textBox);
        Controls.Add(_statusStrip);

        LoadText(path);
    }

    private void LoadText(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (LooksBinary(bytes))
            {
                _textBox.Text = "Файл похож на бинарный. В этой версии F3 показывает только текст.";
                _statusLabel.Text = path;
                return;
            }

            var encoding = DetectEncoding(bytes);
            _textBox.Text = encoding.GetString(SkipBom(bytes, encoding));
            _statusLabel.Text = $"{path}   {bytes.Length:N0} байт   {encoding.EncodingName}";
        }
        catch (Exception ex)
        {
            _textBox.Text = ex.Message;
            _statusLabel.Text = path;
        }
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var sample = Math.Min(bytes.Length, 4096);
        for (var index = 0; index < sample; index++)
        {
            if (bytes[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return IsUtf8(bytes) ? new UTF8Encoding(false) : Encoding.GetEncoding(1251);
    }

    private static byte[] SkipBom(byte[] bytes, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0 || bytes.Length < preamble.Length)
        {
            return bytes;
        }

        for (var index = 0; index < preamble.Length; index++)
        {
            if (bytes[index] != preamble[index])
            {
                return bytes;
            }
        }

        return bytes[preamble.Length..];
    }

    private static bool IsUtf8(byte[] bytes)
    {
        var index = 0;
        while (index < bytes.Length)
        {
            var value = bytes[index];
            if (value <= 0x7F)
            {
                index++;
                continue;
            }

            int expected;
            if ((value & 0xE0) == 0xC0)
            {
                expected = 1;
            }
            else if ((value & 0xF0) == 0xE0)
            {
                expected = 2;
            }
            else if ((value & 0xF8) == 0xF0)
            {
                expected = 3;
            }
            else
            {
                return false;
            }

            if (index + expected >= bytes.Length)
            {
                return false;
            }

            for (var offset = 1; offset <= expected; offset++)
            {
                if ((bytes[index + offset] & 0xC0) != 0x80)
                {
                    return false;
                }
            }

            index += expected + 1;
        }

        return true;
    }
}
