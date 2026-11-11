using Domain.Enums;

namespace Shared.Messaging
{
    // The messages the clinic sends from templates (FR-NTF-02, FR-NTF-03). A key is one message; it has a text per
    // channel and language
    public static class TemplateKeys
    {
        public const string EmailConfirmation = "EmailConfirmation";
        public const string PasswordReset = "PasswordReset";
        public const string AppointmentConfirmed = "AppointmentConfirmed";
        public const string AppointmentReminder = "AppointmentReminder";
    }

    // The names a template may write as {{name}}
    public static class TemplatePlaceholders
    {
        public const string ClinicName = "clinicName";     // the organization's brand name in the message's language
        public const string Name = "name";                 // the recipient's full name
        public const string ConfirmUrl = "confirmUrl";
        public const string ResetUrl = "resetUrl";
        public const string PatientName = "patientName";
        public const string DoctorName = "doctorName";     // without a title: the text writes "Dr."
        public const string Date = "date";                 // written out in the message's language
        public const string Time = "time";
        public const string BranchName = "branchName";
    }

    // The text a template starts with and goes back to when reset. ProviderTemplateName is WhatsApp's: the name the
    // text is approved under at Meta
    public sealed record TemplateDefault(
        TemplateChannel Channel, Language Language, string? Subject, string Body, string? ProviderTemplateName = null);

    // One message: the placeholders its texts may use, the ones a body must keep (a link, the date), and its default
    // text on each channel it is sent by, in both languages
    public sealed class TemplateDefinition(
        string key, IReadOnlyList<string> placeholders, IReadOnlyList<string> requiredPlaceholders, IReadOnlyList<TemplateDefault> defaults)
    {
        public string Key { get; } = key;
        public IReadOnlyList<string> Placeholders { get; } = placeholders;
        public IReadOnlyList<string> RequiredPlaceholders { get; } = requiredPlaceholders;
        public IReadOnlyList<TemplateDefault> Defaults { get; } = defaults;

        public TemplateDefault? DefaultFor(TemplateChannel channel, Language language)
            => Defaults.FirstOrDefault(d => d.Channel == channel && d.Language == language);
    }

    // Every template the system knows, with its default texts. The database holds one row per default (created at
    // startup by DataSeeder.SeedMessageTemplatesAsync), which the clinic may edit; a message with no row is sent with
    // the default. Adding a message: a key, its definition here, and the handler that sends it.
    //
    // An email body is plain text: a blank line starts a paragraph, and [label](url) is a link, drawn as a button
    // when it is alone in its paragraph. The layout around it (header, footer, right-to-left for Arabic) is
    // Services' EmailLayout. SMS and WhatsApp bodies are sent as they are
    public static class MessageTemplateCatalog
    {
        private const string C = TemplatePlaceholders.ClinicName;

        private static readonly string[] AppointmentPlaceholders =
        [
            C, TemplatePlaceholders.PatientName, TemplatePlaceholders.DoctorName, TemplatePlaceholders.Date,
            TemplatePlaceholders.Time, TemplatePlaceholders.BranchName
        ];

        private static readonly string[] AppointmentRequired = [TemplatePlaceholders.Date, TemplatePlaceholders.Time];

        public static readonly IReadOnlyList<TemplateDefinition> All =
        [
            new(TemplateKeys.EmailConfirmation,
                [C, TemplatePlaceholders.Name, TemplatePlaceholders.ConfirmUrl],
                [TemplatePlaceholders.ConfirmUrl],
                [
                    Email(Language.En, "Confirm your email address",
                        """
                        Hello {{name}},

                        Thank you for creating an account with {{clinicName}}. To finish signing up, please confirm your email address.

                        [Confirm email address]({{confirmUrl}})

                        This link expires in 24 hours. If you didn't create this account, you can ignore this email.
                        """),
                    Email(Language.Ar, "تأكيد بريدك الإلكتروني",
                        """
                        مرحبًا {{name}}،

                        شكرًا لإنشاء حساب لدى {{clinicName}}. لإتمام التسجيل، يُرجى تأكيد بريدك الإلكتروني.

                        [تأكيد البريد الإلكتروني]({{confirmUrl}})

                        تنتهي صلاحية هذا الرابط خلال 24 ساعة. إذا لم تكن أنت من أنشأ هذا الحساب، يمكنك تجاهل هذه الرسالة.
                        """),
                ]),

            new(TemplateKeys.PasswordReset,
                [C, TemplatePlaceholders.Name, TemplatePlaceholders.ResetUrl],
                [TemplatePlaceholders.ResetUrl],
                [
                    Email(Language.En, "Reset your password",
                        """
                        Hello {{name}},

                        We received a request to reset the password of your {{clinicName}} account. Choose a new password with the button below.

                        [Reset my password]({{resetUrl}})

                        This link expires in 1 hour and works once. If you didn't ask for it, you can ignore this email and your password stays the same.
                        """),
                    Email(Language.Ar, "إعادة تعيين كلمة المرور",
                        """
                        مرحبًا {{name}}،

                        تلقينا طلبًا لإعادة تعيين كلمة المرور لحسابك لدى {{clinicName}}. اختر كلمة مرور جديدة من الزر التالي.

                        [إعادة تعيين كلمة المرور]({{resetUrl}})

                        تنتهي صلاحية هذا الرابط خلال ساعة واحدة ويُستخدم مرة واحدة فقط. إذا لم تطلب ذلك، يمكنك تجاهل هذه الرسالة وستبقى كلمة مرورك كما هي.
                        """),
                ]),

            new(TemplateKeys.AppointmentConfirmed, AppointmentPlaceholders, AppointmentRequired,
                [
                    Email(Language.En, "Your appointment is confirmed",
                        """
                        Hello {{patientName}},

                        Your appointment at {{clinicName}} is booked.

                        Doctor: Dr. {{doctorName}}
                        Date: {{date}}
                        Time: {{time}}
                        Branch: {{branchName}}

                        Please arrive 10 minutes early. If you need to cancel or reschedule, do it from your account as early as you can.
                        """),
                    Email(Language.Ar, "تم تأكيد موعدك",
                        """
                        مرحبًا {{patientName}}،

                        تم حجز موعدك لدى {{clinicName}}.

                        الطبيب: د. {{doctorName}}
                        التاريخ: {{date}}
                        الوقت: {{time}}
                        الفرع: {{branchName}}

                        يُرجى الحضور قبل الموعد بعشر دقائق. إذا احتجت إلى إلغاء الموعد أو تغييره، يمكنك ذلك من حسابك في أقرب وقت ممكن.
                        """),
                    .. Phone("appointment_confirmed",
                        "{{clinicName}}: your appointment with Dr. {{doctorName}} is booked for {{date}} at {{time}}, {{branchName}}.",
                        "{{clinicName}}: تم حجز موعدك مع د. {{doctorName}} يوم {{date}} الساعة {{time}}، {{branchName}}."),
                ]),

            new(TemplateKeys.AppointmentReminder, AppointmentPlaceholders, AppointmentRequired,
                [
                    Email(Language.En, "Reminder: your appointment on {{date}}",
                        """
                        Hello {{patientName}},

                        This is a reminder of your appointment at {{clinicName}}.

                        Doctor: Dr. {{doctorName}}
                        Date: {{date}}
                        Time: {{time}}
                        Branch: {{branchName}}

                        Please arrive 10 minutes early. You can manage your appointments from your account.
                        """),
                    Email(Language.Ar, "تذكير بموعدك يوم {{date}}",
                        """
                        مرحبًا {{patientName}}،

                        نذكّرك بموعدك لدى {{clinicName}}.

                        الطبيب: د. {{doctorName}}
                        التاريخ: {{date}}
                        الوقت: {{time}}
                        الفرع: {{branchName}}

                        يُرجى الحضور قبل الموعد بعشر دقائق. يمكنك إدارة مواعيدك من حسابك.
                        """),
                    .. Phone("appointment_reminder",
                        "{{clinicName}}: reminder of your appointment with Dr. {{doctorName}} on {{date}} at {{time}}, {{branchName}}.",
                        "{{clinicName}}: نذكّرك بموعدك مع د. {{doctorName}} يوم {{date}} الساعة {{time}}، {{branchName}}."),
                ]),
        ];

        private static readonly Dictionary<string, TemplateDefinition> ByKey = All.ToDictionary(d => d.Key);

        public static TemplateDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

        // A value for every placeholder, for previews (API_CONTRACT.md §13.28)
        public static IReadOnlyDictionary<string, string> Samples(Language language) => language == Language.En ? SamplesEn : SamplesAr;

        private static readonly Dictionary<string, string> SamplesEn = new()
        {
            [TemplatePlaceholders.ClinicName] = "Al Shifa Clinic",
            [TemplatePlaceholders.Name] = "Mona Hassan",
            [TemplatePlaceholders.ConfirmUrl] = "https://clinic.example.com/#/confirm-email?userId=sample&token=sample",
            [TemplatePlaceholders.ResetUrl] = "https://clinic.example.com/#/reset-password?userId=sample&token=sample",
            [TemplatePlaceholders.PatientName] = "Mona Hassan",
            [TemplatePlaceholders.DoctorName] = "Ahmed Kamal",
            [TemplatePlaceholders.Date] = "Sunday, November 15, 2026",
            [TemplatePlaceholders.Time] = "10:30 AM",
            [TemplatePlaceholders.BranchName] = "Maadi Branch",
        };

        private static readonly Dictionary<string, string> SamplesAr = new(SamplesEn)
        {
            [TemplatePlaceholders.ClinicName] = "عيادة الشفاء",
            [TemplatePlaceholders.Name] = "منى حسن",
            [TemplatePlaceholders.PatientName] = "منى حسن",
            [TemplatePlaceholders.DoctorName] = "أحمد كمال",
            [TemplatePlaceholders.Date] = "الأحد، 15 نوفمبر 2026",
            [TemplatePlaceholders.Time] = "10:30 ص",
            [TemplatePlaceholders.BranchName] = "فرع المعادي",
        };

        // Source files may be checked out with CRLF; a stored body always uses \n
        private static TemplateDefault Email(Language language, string subject, string body)
            => new(TemplateChannel.Email, language, subject, body.ReplaceLineEndings("\n"));

        // The same short text by SMS and by WhatsApp, where Meta approves it under the given name
        private static TemplateDefault[] Phone(string whatsAppName, string en, string ar) =>
        [
            new(TemplateChannel.Sms, Language.En, null, en),
            new(TemplateChannel.Sms, Language.Ar, null, ar),
            new(TemplateChannel.WhatsApp, Language.En, null, en, whatsAppName),
            new(TemplateChannel.WhatsApp, Language.Ar, null, ar, whatsAppName),
        ];
    }
}
