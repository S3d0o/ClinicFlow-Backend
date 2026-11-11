namespace Services.Messaging
{
    // How many SMS parts a text is sent (and billed) as. Arabic, like anything outside the GSM alphabet, is sent as
    // UCS-2: 70 characters in one message, 67 in each part of a longer one. Plain Latin text fits 160, or 153 a part
    public static class SmsSegments
    {
        private const string GsmBasic =
            "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

        // Sent as an escape and the character: two places
        private const string GsmExtended = "^{}\\[~]|€\f";

        public static int Count(string text)
        {
            if (text.Length == 0)
                return 0;

            var septets = 0;
            foreach (var character in text)
            {
                if (GsmBasic.Contains(character))
                    septets++;
                else if (GsmExtended.Contains(character))
                    septets += 2;
                else
                    return Parts(text.Length, single: 70, part: 67); // UCS-2 counts UTF-16 units
            }

            return Parts(septets, single: 160, part: 153);
        }

        private static int Parts(int length, int single, int part)
            => length <= single ? 1 : (length + part - 1) / part;
    }
}
