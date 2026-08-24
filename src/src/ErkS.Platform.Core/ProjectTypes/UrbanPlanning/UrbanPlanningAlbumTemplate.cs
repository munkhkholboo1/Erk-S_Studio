namespace ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

/// <summary>ХЕТ төслийн эхний зургийн бүрдлийг альбомын бодит бүтэц болгоно.</summary>
public static class UrbanPlanningAlbumTemplate
{
    public const string MasterPlanTemplateId = "urban-planning-master-plan-v1";
    public const string LegacyPartialPlanTemplateId = "urban-planning-partial-plan-v1";
    public const string PartialPlanTemplateId = "urban-planning-partial-plan-v2";
    public const string DevelopmentProjectTemplateId = "urban-planning-development-project-v1";
    public const string Abbreviation = "ХЕТ";

    public static AlbumDefinition CreateDefinition(string stageType)
    {
        IUrbanPlanningDrawingSequence sequence = UrbanPlanningDrawingSequenceRegistry.Resolve(stageType);
        bool isPartialPlan = stageType.Equals(
            PartialMasterPlanDrawingSequence.StageType,
            StringComparison.OrdinalIgnoreCase);
        bool isDevelopmentProject = stageType.Equals(
            DevelopmentProjectDrawingSequence.StageType,
            StringComparison.OrdinalIgnoreCase);
        if (isDevelopmentProject)
            return CreateDevelopmentProjectDefinition(sequence);

        string fullName = !isPartialPlan
            ? "Хөгжлийн ерөнхий төлөвлөгөө"
            : "Хэсэгчилсэн ерөнхий төлөвлөгөө";

        var definition = new AlbumDefinition
        {
            Title = $"{fullName} ({Abbreviation})",
            TemplateId = isPartialPlan ? PartialPlanTemplateId : MasterPlanTemplateId,
            GeneratedPageFormat = WorkingDrawingAlbumFormatFactory.Create(1, 1),
            IncludeCover = false,
            IncludeTableOfContents = false,
            Sections =
            [
                new AlbumSection { Title = "Ерөнхий төлөвлөгөө / ЕТ" },
                new AlbumSection { Title = "Инженерийн дэд бүтэц / ИДБ" },
            ],
        };

        definition.Composition = sequence.Drawings.Select(slot => new AlbumCompositionItem
        {
            Id = slot.Id,
            Order = slot.Order,
            Number = $"{slot.Mark}-{slot.MarkOrder:00}",
            Title = slot.Title,
            SectionTitle = slot.Mark == UrbanPlanningDrawingMarks.GeneralPlan
                ? "Ерөнхий төлөвлөгөө / ЕТ"
                : "Инженерийн дэд бүтэц / ИДБ",
            Kind = slot.Id is "cover" or "drawing-list-and-notes"
                ? AlbumCompositionKind.Generated
                : AlbumCompositionKind.SourceSlot,
            GeneratedPageKind = slot.Id == "cover" ? AlbumGeneratedPageKind.Cover : AlbumGeneratedPageKind.None,
            Required = slot.Required,
            AllowMultiple = slot.AllowMultiplePages,
            MatchContentKinds = ResolveContentKinds(slot.Id, isPartialPlan),
            MatchNameTerms = ResolveNameTerms(slot.Id, slot.Title, isPartialPlan),
        }).ToList();
        EnsureGeneratedPages(definition);

        return definition;
    }

    public static bool EnsureGeneratedPages(AlbumDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        bool tableOfContentsDropped = false;
        // The composition already carries the drawing list as a page of its
        // own. An album created before that still has the flag set and emits a
        // second, A4-portrait table of contents in the middle of the set.
        if (IsUrbanPlanningTemplate(definition.TemplateId) &&
            definition.IncludeTableOfContents)
        {
            definition.IncludeTableOfContents = false;
            tableOfContentsDropped = true;
        }

        if (!string.Equals(
                definition.TemplateId,
                PartialPlanTemplateId,
                StringComparison.OrdinalIgnoreCase))
        {
            return tableOfContentsDropped;
        }

        AlbumCompositionItem? siteContext = definition.Composition.FirstOrDefault(item =>
            item.Id.Equals("site-context", StringComparison.OrdinalIgnoreCase));
        bool changed = tableOfContentsDropped;

        if (siteContext is null)
        {
            if (definition.Composition.Any(item =>
                    item.Kind == AlbumCompositionKind.SourceSlot && item.Order == 3))
            {
                foreach (AlbumCompositionItem item in definition.Composition.Where(item =>
                             item.Order >= 3))
                {
                    item.Order++;
                }
            }

            siteContext = new AlbumCompositionItem
            {
                Id = "site-context",
                Order = 3,
                Number = "ЕТ-02А",
                Title = "БАЙРШЛЫН СХЕМ / ОРЧНЫ ТОЙМ",
                SectionTitle = "Ерөнхий төлөвлөгөө / ЕТ",
                Kind = AlbumCompositionKind.Generated,
                GeneratedPageKind = AlbumGeneratedPageKind.SiteContext,
                Required = true,
                AllowMultiple = false,
            };
            definition.Composition.Add(siteContext);
            changed = true;
        }
        else if (siteContext.Kind != AlbumCompositionKind.Generated ||
                 siteContext.GeneratedPageKind != AlbumGeneratedPageKind.SiteContext ||
                 !siteContext.Required ||
                 siteContext.AllowMultiple)
        {
            siteContext.Kind = AlbumCompositionKind.Generated;
            siteContext.GeneratedPageKind = AlbumGeneratedPageKind.SiteContext;
            siteContext.Required = true;
            siteContext.AllowMultiple = false;
            siteContext.MatchContentKinds = [];
            siteContext.MatchNameTerms = [];
            changed = true;
        }

        if (changed)
        {
            definition.Composition = definition.Composition
                .OrderBy(item => item.Order)
                .ToList();
        }
        return changed;
    }

    public static bool IsUrbanPlanningTemplate(string? templateId)
    {
        string value = (templateId ?? "").Trim();
        return value.Equals(PartialPlanTemplateId, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(LegacyPartialPlanTemplateId, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(MasterPlanTemplateId, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(DevelopmentProjectTemplateId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Supports(string projectType, string stageType) =>
        projectType.Equals(global::ErkS.Platform.Core.ProjectTypes.UrbanPlanningProjectType.TypeId, StringComparison.OrdinalIgnoreCase) &&
        (stageType.Equals(MasterPlanDrawingSequence.StageType, StringComparison.OrdinalIgnoreCase) ||
         stageType.Equals(PartialMasterPlanDrawingSequence.StageType, StringComparison.OrdinalIgnoreCase) ||
         stageType.Equals(DevelopmentProjectDrawingSequence.StageType, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Барилгажилтын төслийн альбом.
    ///
    /// It differs from its two siblings in three ways, each of which came from
    /// the client rather than from a standard:
    ///
    /// No reference grid, and a corner title block the project chooses. The
    /// other two are working drawings and carry both; this album is composed,
    /// so the page format is left for the project rather than fixed here.
    ///
    /// Sections are the two halves of the reference album - what was surveyed
    /// and what was decided - rather than the ЕТ and ИДБ marks. A drawing's
    /// mark still numbers it; it no longer decides which half it belongs to,
    /// because both halves contain both marks.
    ///
    /// Five opening pages Studio makes for itself, three of them unnumbered.
    /// </summary>
    private static AlbumDefinition CreateDevelopmentProjectDefinition(
        IUrbanPlanningDrawingSequence sequence)
    {
        var definition = new AlbumDefinition
        {
            Title = "Барилгажилтын төсөл",
            TemplateId = DevelopmentProjectTemplateId,
            IncludeCover = false,
            IncludeTableOfContents = false,
            Sections =
            [
                new AlbumSection { Title = DevelopmentProjectDrawingSequence.SurveySectionTitle },
                new AlbumSection { Title = DevelopmentProjectDrawingSequence.SolutionSectionTitle },
            ],
        };

        definition.Composition = sequence.Drawings.Select(slot => new AlbumCompositionItem
        {
            Id = slot.Id,
            Order = slot.Order,
            Number = slot.DrawingNumber,
            Title = slot.Title,
            Scale = slot.Scale,
            SectionTitle = slot.Section,
            Kind = slot.IsGenerated ? AlbumCompositionKind.Generated : AlbumCompositionKind.SourceSlot,
            GeneratedPageKind = slot.GeneratedPageKind,
            Required = slot.Required,
            AllowMultiple = slot.AllowMultiplePages,
            MatchContentKinds = [slot.Id],
            MatchNameTerms = [slot.Title],
        }).ToList();

        return definition;
    }

    public static AlbumCompositionItem? FindSourceSlot(
        AlbumDefinition definition,
        string? number,
        string? contentKind,
        string? name)
    {
        ArgumentNullException.ThrowIfNull(definition);
        List<AlbumCompositionItem> sourceSlots = definition.Composition
            .Where(item => item.Kind == AlbumCompositionKind.SourceSlot)
            .ToList();
        string normalizedContentKind = (contentKind ?? "").Trim();
        AlbumCompositionItem? classified = sourceSlots.FirstOrDefault(item =>
            item.MatchContentKinds.Any(candidate => candidate.Equals(
                normalizedContentKind,
                StringComparison.OrdinalIgnoreCase)));
        if (classified is not null)
        {
            return classified;
        }

        string normalizedNumber = NormalizeDrawingNumber(number);
        if (normalizedNumber.Length > 0)
        {
            AlbumCompositionItem? numbered = sourceSlots.FirstOrDefault(item =>
                NormalizeDrawingNumber(item.Number).Equals(
                    normalizedNumber,
                    StringComparison.Ordinal));
            if (numbered is not null)
            {
                return numbered;
            }
        }

        string searchable = $"{contentKind} {name}".Trim();
        return sourceSlots.FirstOrDefault(item => item.MatchNameTerms.Any(term =>
            searchable.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    public static void MigrateLegacyPages(
        AlbumDefinition definition,
        IEnumerable<AlbumPageDefinition> pages)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(pages);
        bool isPartialPlan = string.Equals(
            definition.TemplateId,
            PartialPlanTemplateId,
            StringComparison.OrdinalIgnoreCase);
        foreach (AlbumPageDefinition page in pages)
        {
            string targetSlotId = isPartialPlan && string.Equals(
                page.TemplateSlotId,
                "engineering-preparation",
                StringComparison.OrdinalIgnoreCase)
                    ? "grading"
                    : page.TemplateSlotId ?? "";
            AlbumCompositionItem? slot = definition.Composition.FirstOrDefault(item =>
                item.Id.Equals(targetSlotId, StringComparison.OrdinalIgnoreCase));
            if (slot is null)
            {
                continue;
            }

            page.TemplateSlotId = slot.Id;
            page.SectionId = definition.Sections.FirstOrDefault(section => section.Title.Equals(
                slot.SectionTitle,
                StringComparison.OrdinalIgnoreCase))?.Id;
        }
    }

    private static List<string> ResolveContentKinds(string id, bool isPartialPlan) =>
        !isPartialPlan ? [id] : id switch
    {
        "development-context" => [id, "development-trend", "planning-context"],
        "existing-condition" => [id, "current-condition", "baseline-assessment"],
        "demographic-economic-analysis" => [id, "socio-economic-analysis"],
        "street-road-transport" => [id, "transport-plan", "traffic-scheme"],
        "pedestrian-movement" => [id, "pedestrian-scheme"],
        "red-lines" => [id, "road-red-lines"],
        "general-plan-zoning" => [id, "planning-solution", "master-plan"],
        "development-projection" => [id, "architecture-spatial", "3d-visualization"],
        "social-service-accessibility" => [id, "public-service-accessibility"],
        "green-infrastructure" => [id, "green-system", "landscaping"],
        "grading" => [id, "engineering-preparation"],
        "first-phase-land-management" => [id, "land-management"],
        "technical-economic-indicators" => [id, "tei"],
        "integrated-engineering-networks" => [id, "engineering-networks"],
        "communications-and-signaling" => [id, "communications-network"],
        _ => [id],
    };

    private static List<string> ResolveNameTerms(string id, string title, bool isPartialPlan) =>
        !isPartialPlan ? [title] : id switch
    {
        "development-context" => [title, "Хөгжлийн чиг хандлага"],
        "existing-condition" => [title, "Өнөөгийн байдал", "Одоогийн байдал"],
        "demographic-economic-analysis" => [title, "Эдийн засгийн тооцоо"],
        "street-road-transport" => [title, "Зам тээврийн төлөвлөлт"],
        "pedestrian-movement" => [title, "Явган хүний замын хөдөлгөөн"],
        "red-lines" => [title, "Улаан шугам"],
        "general-plan-zoning" => [title, "Ерөнхий төлөвлөгөөний бүсчлэл"],
        "development-projection" => [title, "Барилгажилтын төсөөлөл", "Харагдах байдал"],
        "social-service-accessibility" => [title, "Нийгмийн үйлчилгээний хүртээмж"],
        "green-infrastructure" => [title, "Ногоон байгууламж"],
        "grading" => [title, "Өндөржилт", "Инженерийн бэлтгэл"],
        "first-phase-land-management" => [title, "Газар зохион байгуулалтын төлөвлөлт"],
        "technical-economic-indicators" => [title, "Техник эдийн засгийн нэгдсэн үзүүлэлт"],
        "integrated-engineering-networks" => [title, "Инженерийн шугам сүлжээний нэгдсэн"],
        "communications-and-signaling" => [title, "Холбоо мэдээллийн сүлжээ", "Холбоо дохиолол"],
        _ => [title],
    };

    private static string NormalizeDrawingNumber(string? value)
    {
        string normalized = (value ?? "").Trim().ToUpperInvariant();
        if (normalized.StartsWith("IDB", StringComparison.Ordinal))
        {
            normalized = "ИДБ" + normalized[3..];
        }
        else if (normalized.StartsWith("ET", StringComparison.Ordinal))
        {
            normalized = "ЕТ" + normalized[2..];
        }

        return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
    }
}
