﻿using System;
using Orchard;
using Orchard.ContentManagement;
using Orchard.ContentManagement.Drivers;
using Orchard.ContentManagement.Handlers;
using Coevery.Fields.Fields;
using Coevery.Fields.Settings;
using Orchard.Localization;
using Orchard.Utility.Extensions;

namespace Coevery.Fields.Drivers {
    public class DateFieldDriver : ContentFieldDriver<DateField> {
        public IOrchardServices Services { get; set; }
        private const string TemplateName = "Fields/Date.Edit";

        public DateFieldDriver(IOrchardServices services) {
            Services = services;
            T = NullLocalizer.Instance;
            DisplayName = "Date";
            Description = "Allows users to enter date.";
        }

        public Localizer T { get; set; }

        private static string GetPrefix(ContentField field, ContentPart part) {
            return part.PartDefinition.Name + "." + field.Name;
        }

        private static string GetDifferentiator(DateField field, ContentPart part) {
            return field.Name;
        }

        protected override DriverResult Display(ContentPart part, DateField field, string displayType, dynamic shapeHelper) {
            return ContentShape("Fields_Date", GetDifferentiator(field, part), () => {
                var settings = field.PartFieldDefinition.Settings.GetModel<DateFieldSettings>();
                return shapeHelper.Fields_Input().Settings(settings);
            });
        }

        protected override DriverResult Editor(ContentPart part, DateField field, dynamic shapeHelper) {
            return ContentShape("Fields_Date_Edit", GetDifferentiator(field, part),
                () => shapeHelper.EditorTemplate(TemplateName: TemplateName, Model: field, Prefix: GetPrefix(field, part)));
        }

        protected override DriverResult Editor(ContentPart part, DateField field, IUpdateModel updater, dynamic shapeHelper) {
            if (updater.TryUpdateModel(field, GetPrefix(field, part), null, null)) {
                var settings = field.PartFieldDefinition.Settings.GetModel<DateFieldSettings>();

                if (settings.Required && !field.Value.HasValue) {
                    updater.AddModelError(GetPrefix(field, part), T("The field {0} is mandatory.", T(field.DisplayName)));
                }
            }

            return Editor(part, field, shapeHelper);
        }

        protected override void Importing(ContentPart part, DateField field, ImportContentContext context) {
            context.ImportAttribute(field.FieldDefinition.Name + "." + field.Name, "Value", v => field.Value = DateTime.Parse(v));
        }

        protected override void Exporting(ContentPart part, DateField field, ExportContentContext context) {
            context.Element(field.FieldDefinition.Name + "." + field.Name).SetAttributeValue("Value", field.Value.Value.ToShortDateString());
        }

        protected override void Describe(DescribeMembersContext context) {
            context.Member(null, typeof(DateTime), T("Value"), T("The datetime value of the field."));
        }
    }
}
