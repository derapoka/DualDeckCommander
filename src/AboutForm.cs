namespace DualDeckCommander;

/// <summary>Névjegy: leírás, funkciók, jogi nyilatkozat.</summary>
internal sealed class AboutForm : Form
{
    private static int S(int v) => UiScale.S(v);

    public AboutForm()
    {
        var t = Theme.Current;
        Text = "Névjegy – Dual Deck Commander";
        Font = new Font("Segoe UI", 9.5F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = UiScale.S(760, 600);
        MinimumSize = UiScale.S(560, 420);
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        // ---- fejléc logóval ----
        var header = new Panel { Dock = DockStyle.Top, Height = S(124), BackColor = t.SurfaceAlt };
        var logo = new PictureBox
        {
            Image = AppAssets.LoadImage("logo.png"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Bounds = new Rectangle(S(24), S(18), S(88), S(88)),
            BackColor = Color.Transparent,
        };
        var title = new Label
        {
            Text = "Dual Deck Commander",
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(S(128), S(16)),
            ForeColor = t.Text,
            BackColor = Color.Transparent,
        };
        var subtitle = new Label
        {
            Text = "Kétpaneles fájlkezelő emulátorokhoz és lemezképekhez",
            Font = new Font("Segoe UI Semibold", 10.5F),
            AutoSize = true,
            Location = new Point(S(131), S(60)),
            ForeColor = t.TextDim,
            BackColor = Color.Transparent,
        };
        var version = new Label
        {
            Text = "Verzió " + AppAssets.Version,
            AutoSize = true,
            Location = new Point(S(131), S(88)),
            ForeColor = t.Accent,
            BackColor = Color.Transparent,
        };
        header.Controls.AddRange(new Control[] { logo, title, subtitle, version });

        // ---- formázott szöveg ----
        var box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = t.Window,
            ForeColor = t.Text,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false,
            TabStop = false,
        };
        var textHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(24), S(16), S(12), S(8)), BackColor = t.Window };
        textHost.Controls.Add(box);

        // ---- gomb ----
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = S(52), BackColor = t.Surface };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = UiScale.S(100, 32) };
        ok.Location = new Point(bottom.Width - ok.Width - S(16), S(10));
        ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        bottom.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = ok;

        Controls.Add(textHost);
        Controls.Add(bottom);
        Controls.Add(header);

        Fill(box, t);
        ThemeHelper.Apply(this);
        // a ThemeHelper a paneleket Surface színűre állítja – a szöveg háttere maradjon a lista színe
        textHost.BackColor = t.Window;
        box.BackColor = t.Window;
        header.BackColor = t.SurfaceAlt;
        subtitle.ForeColor = t.TextDim;
        version.ForeColor = t.Accent;
        box.HandleCreated += (_, _) => ThemeHelper.SetDarkScrollbars(box, Theme.Current.IsDark);
        Shown += (_, _) => ok.Focus();
    }

    private static void Fill(RichTextBox box, Theme t)
    {
        var normal = new Font("Segoe UI", 10F);
        var bold = new Font("Segoe UI Semibold", 10F);
        var heading = new Font("Segoe UI", 12.5F, FontStyle.Bold);

        void Add(string text, Font f, Color? color = null)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionFont = f;
            box.SelectionColor = color ?? t.Text;
            box.AppendText(text);
        }
        void Heading(string text)
        {
            Add("\n" + text + "\n", heading, t.Text);
        }
        void Para(string text) => Add(text + "\n", normal);
        void Bullet(string label, string text)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionBullet = true;
            box.SelectionIndent = S(8);
            box.BulletIndent = S(14);
            Add(label, bold);
            Add(" " + text + "\n", normal);
            box.SelectionBullet = false;
        }

        Add("Készítette: ", bold);
        Add("Baranyi László\n", normal);
        Add("Licenc: ", bold);
        Add("Freeware (ingyenes)\n", normal);

        Heading("Áttekintés");
        Para("A Dual Deck Commander egy ingyenes, klasszikus kétpaneles felépítésű fájlkezelő, amely kifejezetten az " +
             "emulátoros munkamenetek és a különféle lemezképekkel végzett műveletek megkönnyítésére készült. " +
             "A program segítségével gyorsan és átláthatóan kezelheted a hagyományos fájlokat, miközben közvetlen " +
             "támogatást nyújt számos konzol- és retró számítógépes lemezképformátumhoz is.");

        Heading("Főbb funkciók");
        Bullet("Kétpaneles fájlkezelés:", "Gyors böngészés, másolás, mozgatás és rendezés két egymás melletti panel között.");
        Bullet("Natív Xbox 360 ISO támogatás:", "Natív Xbox 360 ISO lemezképek olvasása, írása és átalakítása.");
        Bullet("Amiga lemezképek kezelése:", "Amiga lemezképformátumok (például ADF, DMS és egyéb archívumok) közvetlen böngészése és olvasása.");
        Bullet("Emulátorbarát munkamenet:", "Kifejezetten olyan feladatokra optimalizálva, ahol gyakran kell különböző " +
                                            "lemezképek és könyvtárak között tartalmat mozgatni vagy ellenőrizni.");

        Heading("Jogi nyilatkozat (Disclaimer)");
        Para("A szoftver ingyenesen, „ahogy van” (AS-IS) alapon, bármiféle kifejezett vagy hallgatólagos " +
             "garanciavállalás nélkül kerül terjesztésre és felhasználásra. A szerző nem vállal felelősséget " +
             "semmilyen adatvesztésért, fájlsérülésért, illetve közvetlen vagy közvetett kárért, amely a program " +
             "használatából vagy használatának képtelenségéből ered.");
        Para("");
        Para("A Dual Deck Commander független, nem hivatalos eszköz: nem áll kapcsolatban a Microsofttal, az Amiga/Commodore " +
             "jogtulajdonosaival vagy más említett gyártókkal és védjegytulajdonosokkal. A program nem támogatja és " +
             "nem népszerűsíti a szerzői jogvédelem alatt álló tartalmak jogosulatlan másolását, terjesztését vagy a " +
             "másolásvédelmi mechanizmusok megkerülését.");
        Para("");
        Para("A felhasználó kizárólagos felelőssége, hogy a szoftverrel kezelt fájlok és lemezképek tekintetében " +
             "rendelkezzen a szükséges jogosultságokkal, és betartsa a hatályos szerzői jogi és egyéb jogszabályokat.");

        Heading("Kapcsolat / Szerző");
        Para("Baranyi László");

        Add("\nFelhasznált összetevők: SharpCompress (MIT licenc) – RAR és 7-Zip kibontás;\n" +
            "iso2god-rs (MIT licenc) – GoD fejlécsablon és Xbox címadatbázis.\n",
            new Font("Segoe UI", 8.5F), t.TextDim);

        box.SelectionStart = 0;
        box.SelectionLength = 0;
    }
}
