using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ComplexNutrition.UI
{
    /// <summary>
    /// The game's confirmation box (same size, buttons and title) with an optional tip on its own line at the bottom,
    /// in red. Vanilla's box draws its text in one font and colour, so it cannot show the tip apart.
    /// </summary>
    public class GuiDialogConfirmTip : GuiDialog
    {
        private const double TextWidth = 500;
        public static readonly double[] TipColor = { 0.93, 0.42, 0.37, 1.0 };

        private readonly string text;
        private readonly string tip;
        private readonly Action<bool> onAnswer;

        public override double DrawOrder => 2.0;
        public override string ToggleKeyCombinationCode => null;

        public GuiDialogConfirmTip(ICoreClientAPI capi, string text, string tip, Action<bool> onAnswer) : base(capi)
        {
            this.text = text;
            this.tip = tip;
            this.onAnswer = onAnswer;
            Compose();
        }

        private void Compose()
        {
            var font = CairoFont.WhiteSmallText();
            var tipFont = CairoFont.WhiteSmallText().WithColor(TipColor);
            var measure = new TextDrawUtil();
            double textHeight = measure.GetMultilineTextHeight(font, text, TextWidth);
            double tipHeight = string.IsNullOrEmpty(tip) ? 0 : measure.GetMultilineTextHeight(tipFont, tip, TextWidth) + 12;

            var textBounds = ElementStdBounds.Rowed(0.4f, 0, EnumDialogArea.LeftFixed).WithFixedWidth(TextWidth);
            var tipBounds = ElementBounds.Fixed(0, textBounds.fixedY + textHeight + 12, TextWidth, tipHeight);
            var bg = ElementStdBounds.DialogBackground().WithFixedPadding(GuiStyle.ElementToDialogPadding, GuiStyle.ElementToDialogPadding);
            float buttonRow = (float)((textHeight + tipHeight + 80) / 80);

            var composer = capi.Gui.CreateCompo("complexnutrition-confirm", ElementStdBounds.AutosizedMainDialog)
                .AddShadedDialogBG(bg)
                .AddDialogTitleBar(Lang.Get("Please Confirm"), () => TryClose())
                .BeginChildElements(bg)
                .AddStaticText(text, font, textBounds);
            if (!string.IsNullOrEmpty(tip)) composer.AddStaticText(tip, tipFont, tipBounds);
            SingleComposer = composer
                .AddSmallButton(Lang.Get("Cancel"), () => Answer(false), ElementStdBounds.MenuButton(buttonRow).WithAlignment(EnumDialogArea.LeftFixed).WithFixedPadding(6.0))
                .AddSmallButton(Lang.Get("Confirm"), () => Answer(true), ElementStdBounds.MenuButton(buttonRow).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(6.0))
                .EndChildElements()
                .Compose();
        }

        private bool Answer(bool ok)
        {
            onAnswer?.Invoke(ok);
            TryClose();
            return true;
        }

        public override void OnGuiOpened()
        {
            Compose();
            base.OnGuiOpened();
        }
    }
}
