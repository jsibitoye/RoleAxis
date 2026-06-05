using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace RoleAxis.Career.InterviewAssistant;

internal sealed class ResumeAnalysisInput
{
    public string ResumeText { get; set; } = "";
    public string TargetRole { get; set; } = "";
    public string TargetIndustry { get; set; } = "";
    public string SeniorityLevel { get; set; } = "Mid-level";
    public string ResumeType { get; set; } = "General";
    public string JobDescription { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string JobUrl { get; set; } = "";
    public string OutputStyle { get; set; } = "ATS optimized";
}

internal sealed class ResumeAnalysisResult
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ResumeScoreSummary Scores { get; set; } = new();
    public ResumeMetadata Metadata { get; set; } = new();
    public string ExecutiveSummary { get; set; } = "";
    public List<ResumeFinding> AtsFindings { get; set; } = new();
    public List<SkillFinding> Skills { get; set; } = new();
    public List<SkillGapFinding> SkillGaps { get; set; } = new();
    public List<ResumeFinding> ExperienceFindings { get; set; } = new();
    public List<BulletImprovement> WeakBullets { get; set; } = new();
    public List<KeywordFinding> Keywords { get; set; } = new();
    public List<ResumeFinding> RecruiterRisks { get; set; } = new();
    public List<ActionPlanItem> RecommendationPlan { get; set; } = new();
    public string JobMatchSummary { get; set; } = "";
    public string UpgradedResume { get; set; } = "";
    public string TailoredResume { get; set; } = "";
    public string CoverLetter { get; set; } = "";
    public string InterviewPrep { get; set; } = "";
}

internal sealed class ResumeScoreSummary
{
    public int Overall { get; set; }
    public int Ats { get; set; }
    public int KeywordAlignment { get; set; }
    public int Impact { get; set; }
    public int Clarity { get; set; }
    public int Leadership { get; set; }
    public int TechnicalDepth { get; set; }
    public int Formatting { get; set; }
    public int JobMatch { get; set; }
}

internal sealed class ResumeMetadata
{
    public string CandidateName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Location { get; set; } = "";
    public string LinkedIn { get; set; } = "";
    public string GitHubOrPortfolio { get; set; } = "";
    public string YearsExperienceEstimate { get; set; } = "";
    public string TargetRoleAlignment { get; set; } = "";
}

internal sealed class ResumeFinding
{
    public string Section { get; set; } = "";
    public string Finding { get; set; } = "";
    public string Severity { get; set; } = "";
    public string WhyItMatters { get; set; } = "";
    public string RecommendedFix { get; set; } = "";
}

internal sealed class SkillFinding
{
    public string Skill { get; set; } = "";
    public string Category { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Strength { get; set; } = "";
    public string Recommendation { get; set; } = "";
}

internal sealed class SkillGapFinding
{
    public string RequiredSkill { get; set; } = "";
    public string FoundInResume { get; set; } = "";
    public string Strength { get; set; } = "";
    public string GapSeverity { get; set; } = "";
    public string Recommendation { get; set; } = "";
}

internal sealed class BulletImprovement
{
    public string OriginalBullet { get; set; } = "";
    public string Problem { get; set; } = "";
    public string ImprovedBullet { get; set; } = "";
    public string Reason { get; set; } = "";
}

internal sealed class KeywordFinding
{
    public string Keyword { get; set; } = "";
    public string InResume { get; set; } = "";
    public string Importance { get; set; } = "";
    public string SuggestedPlacement { get; set; } = "";
}

internal sealed class ActionPlanItem
{
    public int Priority { get; set; }
    public string Action { get; set; } = "";
    public string ExpectedImpact { get; set; } = "";
    public string Difficulty { get; set; } = "";
}

internal sealed class SavedJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public string RemoteType { get; set; } = "Any";
    public string SalaryRange { get; set; } = "";
    public string JobUrl { get; set; } = "";
    public string Source { get; set; } = "Manual";
    public string JobDescription { get; set; } = "";
    public string Status { get; set; } = "Saved";
    public int MatchScore { get; set; }
    public DateTime DateSaved { get; set; } = DateTime.Now;
    public DateTime? DateApplied { get; set; }
    public string Notes { get; set; } = "";
    public string ResumeVersionPath { get; set; } = "";
    public string CoverLetterPath { get; set; } = "";
}

internal sealed class JobMatchReport
{
    public int OverallFitScore { get; set; }
    public int SkillsMatch { get; set; }
    public int ExperienceMatch { get; set; }
    public int KeywordMatch { get; set; }
    public int SeniorityMatch { get; set; }
    public int DomainMatch { get; set; }
    public string Strategy { get; set; } = "";
    public List<SkillGapFinding> SkillGaps { get; set; } = new();
    public List<ResumeFinding> RequirementFindings { get; set; } = new();
    public List<ResumeFinding> Risks { get; set; } = new();
}

internal sealed class ResumeAnalysisService
{
    private static readonly Dictionary<string, string[]> SkillCatalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Programming Languages"] = new[] { "c#", ".net", "python", "javascript", "typescript", "java", "sql", "powershell", "bash", "go", "rust", "php", "ruby" },
        ["Cloud / Security / Networking"] = new[] { "aws", "azure", "gcp", "cloud", "kubernetes", "docker", "terraform", "security", "iam", "network", "firewall", "zero trust", "soc", "siem" },
        ["Tools / Platforms"] = new[] { "git", "github", "jira", "salesforce", "servicenow", "linux", "windows", "tableau", "power bi", "excel", "figma", "slack" },
        ["Technical Skills"] = new[] { "api", "microservices", "database", "etl", "analytics", "automation", "testing", "ci/cd", "devops", "machine learning", "ai" },
        ["Leadership Skills"] = new[] { "led", "managed", "mentored", "stakeholder", "strategy", "roadmap", "hiring", "cross-functional", "executive" },
        ["Business Skills"] = new[] { "revenue", "cost", "growth", "operations", "process", "budget", "forecast", "customer", "sales", "compliance" },
        ["Communication Skills"] = new[] { "presentation", "documentation", "training", "collaboration", "negotiation", "workshop", "facilitation" },
        ["Certifications"] = new[] { "certified", "certification", "aws certified", "pmp", "scrum", "comptia", "cissp", "cisa", "cfa", "shrm" },
        ["Domain Skills"] = new[] { "healthcare", "finance", "legal", "immigration", "education", "saas", "ecommerce", "manufacturing", "government" }
    };

    private static readonly string[] WeakVerbs = { "helped", "worked", "responsible", "assisted", "handled", "participated", "involved", "supported" };
    private static readonly string[] StrongVerbs = { "Led", "Built", "Improved", "Reduced", "Delivered", "Automated", "Launched", "Optimized", "Designed", "Implemented" };
    private static readonly Regex EmailRegex = new(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"(\+?\d[\d\s().-]{8,}\d)", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://[^\s]+|(?:linkedin|github)\.com/[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MetricRegex = new(@"\b(\d+%?|\$\d+|\d+x|\d+\+|million|thousand|reduced|increased|improved|saved|grew|cut|faster|revenue|cost)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string> LoadResumeFileAsync(string path, CancellationToken token = default)
    {
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".txt" || extension == ".rtf")
                return File.ReadAllText(path);

            if (extension == ".docx")
            {
                using var doc = WordprocessingDocument.Open(path, false);
                return doc.MainDocumentPart?.Document?.Body?.InnerText ?? "";
            }

            if (extension == ".pdf")
            {
                var builder = new StringBuilder();
                using var pdf = PdfDocument.Open(path);
                foreach (var page in pdf.GetPages())
                {
                    token.ThrowIfCancellationRequested();
                    builder.AppendLine(page.Text);
                }
                return builder.ToString();
            }

            throw new NotSupportedException("Supported resume files are TXT, RTF, DOCX, and PDF.");
        }, token);
    }

    public Task<ResumeAnalysisResult> AnalyzeAsync(ResumeAnalysisInput input, CancellationToken token = default)
    {
        return Task.Run(() => Analyze(input, token), token);
    }

    public ResumeAnalysisResult Analyze(ResumeAnalysisInput input, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        string resume = input.ResumeText ?? "";
        string lower = resume.ToLowerInvariant();
        var result = new ResumeAnalysisResult
        {
            Metadata = ExtractMetadata(resume, input),
            Skills = ExtractSkills(resume),
            WeakBullets = AnalyzeBullets(resume, input),
            AtsFindings = AnalyzeAts(resume, input),
            ExperienceFindings = AnalyzeExperience(resume, input),
            Keywords = AnalyzeKeywords(resume, input.JobDescription),
            RecruiterRisks = AnalyzeRecruiterRisks(resume, input)
        };

        result.SkillGaps = AnalyzeSkillGaps(resume, input.JobDescription, input.TargetRole);
        result.RecommendationPlan = BuildRecommendationPlan(result);

        int wordCount = Tokenize(resume).Count;
        bool hasSummary = ContainsAny(lower, "summary", "profile", "objective");
        bool hasSkills = ContainsAny(lower, "skills", "technical skills", "core competencies");
        bool hasExperience = ContainsAny(lower, "experience", "employment", "work history");
        bool hasEducation = ContainsAny(lower, "education", "degree", "university", "college");
        bool hasMetrics = MetricRegex.IsMatch(resume);
        int highStrengthSkills = result.Skills.Count(skill => skill.Strength == "Strong");
        int missingCritical = result.SkillGaps.Count(gap => gap.GapSeverity == "Critical");
        int foundKeywords = result.Keywords.Count(keyword => keyword.InResume == "Yes");
        int totalKeywords = Math.Max(1, result.Keywords.Count);

        var scores = new ResumeScoreSummary
        {
            Ats = ClampScore(45 + (hasSummary ? 10 : 0) + (hasSkills ? 12 : 0) + (hasExperience ? 12 : 0) + (hasEducation ? 6 : 0) + (wordCount is > 350 and < 1100 ? 10 : 0) - result.AtsFindings.Count * 4),
            KeywordAlignment = ClampScore(result.Keywords.Count == 0 ? 62 + highStrengthSkills * 3 : 35 + (int)Math.Round(foundKeywords * 60d / totalKeywords)),
            Impact = ClampScore(42 + result.WeakBullets.Count(bullet => bullet.Problem.Contains("metric", StringComparison.OrdinalIgnoreCase)) * -5 + (hasMetrics ? 28 : 0)),
            Clarity = ClampScore(72 - result.WeakBullets.Count(bullet => bullet.Problem.Contains("too long", StringComparison.OrdinalIgnoreCase)) * 4 - result.AtsFindings.Count * 2),
            Leadership = ClampScore(45 + CountMatches(lower, SkillCatalog["Leadership Skills"]) * 6),
            TechnicalDepth = ClampScore(45 + CountMatches(lower, SkillCatalog["Technical Skills"].Concat(SkillCatalog["Cloud / Security / Networking"])) * 4),
            Formatting = ClampScore(72 + (wordCount is > 350 and < 1100 ? 10 : -8) - CountLongLines(resume) * 2),
            JobMatch = string.IsNullOrWhiteSpace(input.JobDescription) ? 0 : ClampScore(45 + (int)Math.Round(foundKeywords * 50d / totalKeywords) - missingCritical * 8)
        };
        scores.Overall = ClampScore((scores.Ats + scores.KeywordAlignment + scores.Impact + scores.Clarity + scores.Leadership + scores.TechnicalDepth + scores.Formatting + (scores.JobMatch > 0 ? scores.JobMatch : scores.KeywordAlignment)) / 8);
        result.Scores = scores;

        result.ExecutiveSummary = BuildExecutiveSummary(input, result);
        result.JobMatchSummary = BuildJobMatchSummary(input, result);
        return result;
    }

    public string GenerateUpgradedResume(ResumeAnalysisInput input, ResumeAnalysisResult analysis)
    {
        var metadata = analysis.Metadata;
        var builder = new StringBuilder();
        builder.AppendLine(DisplayOrFallback(metadata.CandidateName, "[Candidate Name]"));
        builder.AppendLine(string.Join(" | ", new[]
        {
            metadata.Email,
            metadata.Phone,
            metadata.Location,
            metadata.LinkedIn,
            metadata.GitHubOrPortfolio
        }.Where(value => !string.IsNullOrWhiteSpace(value))));
        builder.AppendLine();
        builder.AppendLine(DisplayOrFallback(input.TargetRole, "Target Role") + " | " + DisplayOrFallback(input.OutputStyle, "ATS optimized"));
        builder.AppendLine();
        builder.AppendLine("PROFESSIONAL SUMMARY");
        builder.AppendLine(BuildSummarySentence(input, analysis));
        builder.AppendLine();
        builder.AppendLine("CORE SKILLS");
        foreach (var group in analysis.Skills.GroupBy(skill => skill.Category).Take(6))
            builder.AppendLine(group.Key + ": " + string.Join(", ", group.Select(skill => skill.Skill).Take(8)));
        if (analysis.Skills.Count == 0)
            builder.AppendLine("[Add 8 to 12 role-relevant skills pulled from your verified experience.]");
        builder.AppendLine();
        builder.AppendLine("SELECTED ACHIEVEMENTS");
        foreach (var bullet in analysis.WeakBullets.Take(5))
            builder.AppendLine("- " + bullet.ImprovedBullet);
        if (analysis.WeakBullets.Count == 0)
            builder.AppendLine("- Delivered [project/result] that improved [business/technical outcome] by [insert verified metric].");
        builder.AppendLine();
        builder.AppendLine("PROFESSIONAL EXPERIENCE");
        builder.AppendLine(ExtractExperienceBlock(input.ResumeText));
        builder.AppendLine();
        builder.AppendLine("PROJECTS / TECHNICAL PROOF");
        builder.AppendLine("- Add the strongest project, platform, implementation, portfolio, or case study that proves " + DisplayOrFallback(input.TargetRole, "the target role") + " readiness.");
        builder.AppendLine();
        builder.AppendLine("EDUCATION / CERTIFICATIONS");
        builder.AppendLine(ExtractEducationBlock(input.ResumeText));
        builder.AppendLine();
        builder.AppendLine("CHANGE SUMMARY");
        builder.AppendLine("- Repositioned the resume around the target role.");
        builder.AppendLine("- Added an ATS-friendly skills section and stronger achievement framing.");
        builder.AppendLine("- Preserved truthfulness by using bracketed prompts where verified metrics are missing.");
        builder.AppendLine();
        builder.AppendLine("WHY THIS VERSION IS STRONGER");
        builder.AppendLine("- Recruiters can understand the target role, strongest skills, and proof faster.");
        builder.AppendLine("- Weak responsibility bullets are reframed as outcome bullets.");
        builder.AppendLine("- Keyword placement is more natural and easier for ATS systems to parse.");
        builder.AppendLine();
        builder.AppendLine("KEYWORDS ADDED");
        builder.AppendLine(string.Join(", ", analysis.Keywords.Where(k => k.InResume == "No").Take(12).Select(k => k.Keyword)));
        builder.AppendLine();
        builder.AppendLine("REMAINING GAPS");
        foreach (var gap in analysis.SkillGaps.Where(g => g.GapSeverity is "Critical" or "Important").Take(6))
            builder.AppendLine("- " + gap.RequiredSkill + ": " + gap.Recommendation);
        builder.AppendLine();
        builder.AppendLine("INTERVIEW TALKING POINTS FROM RESUME");
        builder.AppendLine("- Be ready to explain the strongest recent achievement with context, action, result, and metric.");
        builder.AppendLine("- Be ready to connect your experience directly to " + DisplayOrFallback(input.TargetRole, "the target role") + ".");
        builder.AppendLine("- Be ready to explain gaps honestly and show how you are closing them.");
        return builder.ToString().Trim();
    }

    public string GenerateTailoredResume(ResumeAnalysisInput input, ResumeAnalysisResult analysis, SavedJob? job = null)
    {
        string jobTitle = DisplayOrFallback(job?.Title ?? input.TargetRole, "Target Role");
        string company = DisplayOrFallback(job?.Company ?? input.CompanyName, "Target Company");
        var builder = new StringBuilder();
        builder.AppendLine("TAILORED RESUME FOR " + jobTitle.ToUpperInvariant());
        builder.AppendLine("Company: " + company);
        builder.AppendLine();
        builder.AppendLine("TARGETED SUMMARY");
        builder.AppendLine(BuildSummarySentence(input, analysis) + " This version emphasizes " + jobTitle + " alignment, job keywords, and evidence-backed achievements.");
        builder.AppendLine();
        builder.AppendLine("ROLE-SPECIFIC SKILLS");
        foreach (var keyword in analysis.Keywords.Where(k => k.Importance != "Low").Take(16))
            builder.AppendLine("- " + keyword.Keyword + " - " + keyword.SuggestedPlacement);
        builder.AppendLine();
        builder.AppendLine("REORDERED EXPERIENCE EMPHASIS");
        foreach (var bullet in analysis.WeakBullets.Take(6))
            builder.AppendLine("- " + bullet.ImprovedBullet);
        builder.AppendLine();
        builder.AppendLine("MISSING METRIC PROMPTS");
        builder.AppendLine("- Add verified numbers for scale, revenue, cost, quality, speed, customers, team size, or uptime where available.");
        builder.AppendLine("- Replace bracketed prompts only with truthful metrics.");
        builder.AppendLine();
        builder.AppendLine("JOB FIT SUMMARY");
        builder.AppendLine(analysis.JobMatchSummary);
        return builder.ToString().Trim();
    }

    private static ResumeMetadata ExtractMetadata(string resume, ResumeAnalysisInput input)
    {
        var lines = resume.SplitLines().Select(line => line.Trim()).Where(line => line.Length > 0).Take(12).ToList();
        string firstNameLike = lines.FirstOrDefault(line =>
            !EmailRegex.IsMatch(line) &&
            !PhoneRegex.IsMatch(line) &&
            line.Length < 70 &&
            line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 2 and <= 5) ?? "";

        string urlText = string.Join(" ", UrlRegex.Matches(resume).Select(match => match.Value));
        string linkedIn = UrlRegex.Matches(resume).Select(match => match.Value).FirstOrDefault(value => value.Contains("linkedin", StringComparison.OrdinalIgnoreCase)) ?? "";
        string github = UrlRegex.Matches(resume).Select(match => match.Value).FirstOrDefault(value => value.Contains("github", StringComparison.OrdinalIgnoreCase)) ?? "";
        string portfolio = string.IsNullOrWhiteSpace(github)
            ? UrlRegex.Matches(resume).Select(match => match.Value).FirstOrDefault(value => !value.Contains("linkedin", StringComparison.OrdinalIgnoreCase)) ?? ""
            : github;

        return new ResumeMetadata
        {
            CandidateName = firstNameLike,
            Email = EmailRegex.Match(resume).Value,
            Phone = PhoneRegex.Match(resume).Value,
            Location = DetectLocation(lines),
            LinkedIn = linkedIn,
            GitHubOrPortfolio = portfolio,
            YearsExperienceEstimate = EstimateYearsExperience(resume),
            TargetRoleAlignment = string.IsNullOrWhiteSpace(input.TargetRole)
                ? "Add a target role to measure alignment."
                : (resume.Contains(input.TargetRole, StringComparison.OrdinalIgnoreCase) || urlText.Contains(input.TargetRole, StringComparison.OrdinalIgnoreCase)
                    ? "Target role appears directly in resume or links."
                    : "Target role is not explicit. Add a headline aligned to " + input.TargetRole + ".")
        };
    }

    private static List<SkillFinding> ExtractSkills(string resume)
    {
        var findings = new List<SkillFinding>();
        string lower = resume.ToLowerInvariant();
        foreach (var group in SkillCatalog)
        {
            foreach (string skill in group.Value)
            {
                int count = CountOccurrences(lower, skill.ToLowerInvariant());
                if (count <= 0)
                    continue;
                findings.Add(new SkillFinding
                {
                    Skill = skill,
                    Category = group.Key,
                    Evidence = count == 1 ? "Mentioned once" : "Mentioned " + count + " times",
                    Strength = count >= 3 ? "Strong" : count == 2 ? "Moderate" : "Light",
                    Recommendation = count >= 2 ? "Keep, but anchor it with proof if possible." : "Add context or an achievement showing how this skill was used."
                });
            }
        }
        return findings
            .GroupBy(item => item.Skill, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Skill)
            .ToList();
    }

    private static List<ResumeFinding> AnalyzeAts(string resume, ResumeAnalysisInput input)
    {
        string lower = resume.ToLowerInvariant();
        var items = new List<ResumeFinding>();
        AddIf(items, !EmailRegex.IsMatch(resume), "Contact", "Email address missing", "Critical", "Recruiters and ATS systems need a direct contact path.", "Add a professional email at the top.");
        AddIf(items, !PhoneRegex.IsMatch(resume), "Contact", "Phone number missing", "Important", "Many recruiters still call first for screening.", "Add a mobile number near the email.");
        AddIf(items, !ContainsAny(lower, "skills", "core competencies", "technical skills"), "Skills", "Missing clear skills section", "Critical", "ATS systems weigh explicit skill sections heavily.", "Add a concise skills section aligned to the target job.");
        AddIf(items, !ContainsAny(lower, "summary", "profile", "objective"), "Summary", "No professional summary or headline", "Important", "Recruiters need to understand the target role within seconds.", "Add a 3 to 4 line summary positioned to the target role.");
        AddIf(items, !MetricRegex.IsMatch(resume), "Impact", "No measurable achievements detected", "Critical", "Achievement metrics prove level, scope, and value.", "Add verified numbers for scale, quality, revenue, cost, time, or users.");
        AddIf(items, CountLongLines(resume) > 8, "Formatting", "Many long lines detected", "Important", "Dense text is hard to scan and may parse poorly.", "Break long bullets into cleaner achievement bullets.");
        AddIf(items, !ContainsAny(lower, "education", "degree", "university", "college", "certification"), "Credentials", "Education or certifications are unclear", "Nice-to-have", "Some roles require credentials or proof of training.", "Add education and certifications if relevant.");
        AddIf(items, !string.IsNullOrWhiteSpace(input.TargetRole) && !lower.Contains(input.TargetRole.ToLowerInvariant()), "Role Alignment", "Target role is not explicit", "Important", "A generic resume can look unfocused.", "Add a headline or summary that names the target role.");
        return items;
    }

    private static List<SkillGapFinding> AnalyzeSkillGaps(string resume, string jobDescription, string targetRole)
    {
        string source = string.IsNullOrWhiteSpace(jobDescription) ? targetRole : jobDescription;
        if (string.IsNullOrWhiteSpace(source))
            return new List<SkillGapFinding>();

        var required = ExtractImportantTerms(source).Take(32).ToList();
        string lowerResume = resume.ToLowerInvariant();
        return required.Select(term =>
        {
            bool found = lowerResume.Contains(term.ToLowerInvariant());
            string severity = found ? "Covered" : IsLikelyCritical(term) ? "Critical" : "Important";
            return new SkillGapFinding
            {
                RequiredSkill = term,
                FoundInResume = found ? "Yes" : "No",
                Strength = found ? "Evidence present" : "Not visible",
                GapSeverity = severity,
                Recommendation = found
                    ? "Keep this keyword close to proof in experience bullets."
                    : "Add truthful evidence, project proof, or a learning plan for " + term + "."
            };
        }).ToList();
    }

    private static List<ResumeFinding> AnalyzeExperience(string resume, ResumeAnalysisInput input)
    {
        string lower = resume.ToLowerInvariant();
        var items = new List<ResumeFinding>();
        AddIf(items, !ContainsAny(lower, "led", "managed", "owned", "directed", "mentored"), "Leadership", "Leadership evidence is light", "Important", "Senior roles need proof of ownership, influence, or direction.", "Add examples of leadership, mentoring, stakeholder ownership, or project accountability.");
        AddIf(items, !ContainsAny(lower, "project", "launched", "implemented", "built", "delivered"), "Projects", "Project evidence is unclear", "Important", "Hiring teams want proof you can deliver outcomes, not just perform tasks.", "Add project bullets with scope, tools, and business result.");
        AddIf(items, !MetricRegex.IsMatch(resume), "Business Impact", "Business impact is not quantified", "Critical", "Metrics help recruiters defend your candidacy.", "Add truthful metrics or bracketed metric prompts before finalizing.");
        AddIf(items, !ContainsAny(lower, "stakeholder", "customer", "client", "executive", "cross-functional"), "Collaboration", "Stakeholder evidence is thin", "Nice-to-have", "Many jobs require influence outside the immediate team.", "Show collaboration with customers, leaders, or cross-functional teams.");
        AddIf(items, CountOccurrences(lower, "responsible for") > 2, "Bullet Framing", "Too many responsibility statements", "Important", "Responsibilities sound passive compared with achievements.", "Convert duties into action plus result bullets.");
        return items;
    }

    private static List<BulletImprovement> AnalyzeBullets(string resume, ResumeAnalysisInput input)
    {
        var bullets = resume.SplitLines()
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("-") || line.StartsWith("*") || line.StartsWith("\u2022") || Regex.IsMatch(line, @"^[A-Za-z]+ed\b"))
            .Select(line => line.TrimStart('-', '*', '\u2022', ' '))
            .Where(line => line.Length > 12)
            .Take(50)
            .ToList();

        var findings = new List<BulletImprovement>();
        foreach (string bullet in bullets)
        {
            var problems = new List<string>();
            string lower = bullet.ToLowerInvariant();
            if (WeakVerbs.Any(verb => lower.StartsWith(verb)))
                problems.Add("starts with a weak verb");
            if (!MetricRegex.IsMatch(bullet))
                problems.Add("missing measurable impact");
            if (bullet.Length > 190)
                problems.Add("too long to scan quickly");
            if (bullet.Length < 45)
                problems.Add("too short to prove value");
            if (ContainsAny(lower, "responsible for", "duties included"))
                problems.Add("sounds like responsibility instead of achievement");

            if (problems.Count == 0)
                continue;

            findings.Add(new BulletImprovement
            {
                OriginalBullet = bullet,
                Problem = string.Join("; ", problems),
                ImprovedBullet = ImproveBullet(bullet, input.TargetRole),
                Reason = "The rewrite uses action, scope, and outcome framing while leaving missing metrics as truthful prompts."
            });
        }
        return findings.Take(18).ToList();
    }

    private static List<KeywordFinding> AnalyzeKeywords(string resume, string jobDescription)
    {
        if (string.IsNullOrWhiteSpace(jobDescription))
            return new List<KeywordFinding>();

        string lowerResume = resume.ToLowerInvariant();
        return ExtractImportantTerms(jobDescription)
            .Take(36)
            .Select((keyword, index) => new KeywordFinding
            {
                Keyword = keyword,
                InResume = lowerResume.Contains(keyword.ToLowerInvariant()) ? "Yes" : "No",
                Importance = index < 12 ? "High" : index < 24 ? "Medium" : "Low",
                SuggestedPlacement = index < 12 ? "Summary, skills, and most recent role" : "Skills or relevant project bullet"
            })
            .ToList();
    }

    private static List<ResumeFinding> AnalyzeRecruiterRisks(string resume, ResumeAnalysisInput input)
    {
        string lower = resume.ToLowerInvariant();
        var risks = new List<ResumeFinding>();
        AddIf(risks, string.IsNullOrWhiteSpace(input.TargetRole), "Targeting", "Unclear target role", "Critical", "Recruiters may not know what role to map you to.", "Add a target role and tailor the headline.");
        AddIf(risks, !MetricRegex.IsMatch(resume), "Proof", "No metrics visible", "Critical", "A resume with no numbers can look junior or generic.", "Add verified metrics to the top three recent roles.");
        AddIf(risks, CountOccurrences(lower, "responsible") > 2, "Positioning", "Too many responsibility phrases", "Important", "Responsibility language is weaker than achievement language.", "Use stronger verbs and outcome language.");
        AddIf(risks, !ContainsAny(lower, "recent", "2024", "2025", "2026", "current", "present"), "Freshness", "Recent proof may be unclear", "Important", "Recruiters want evidence that skills are current.", "Clarify dates and add recent projects or tools.");
        AddIf(risks, !ContainsAny(lower, "tools", "skills", "technologies", "platforms"), "ATS", "Tools section may be missing", "Important", "Tool keywords often drive ATS matching.", "Add a clean tools/platforms section.");
        return risks;
    }

    private static List<ActionPlanItem> BuildRecommendationPlan(ResumeAnalysisResult result)
    {
        var plan = new List<ActionPlanItem>();
        int priority = 1;
        if (result.Scores.Impact < 70)
            plan.Add(new ActionPlanItem { Priority = priority++, Action = "Add measurable impact to the top three recent roles.", ExpectedImpact = "Higher recruiter confidence and stronger seniority signal.", Difficulty = "Medium" });
        if (result.Scores.Ats < 75)
            plan.Add(new ActionPlanItem { Priority = priority++, Action = "Add a clean ATS-friendly headline, summary, skills, experience, and education structure.", ExpectedImpact = "Better parsing and faster recruiter scan.", Difficulty = "Low" });
        if (result.SkillGaps.Any(gap => gap.GapSeverity == "Critical"))
            plan.Add(new ActionPlanItem { Priority = priority++, Action = "Address critical job-description skill gaps with truthful evidence or project proof.", ExpectedImpact = "Improves match score and lowers rejection risk.", Difficulty = "Medium" });
        if (result.WeakBullets.Count > 0)
            plan.Add(new ActionPlanItem { Priority = priority++, Action = "Rewrite weak bullets using action, scope, result, and verified metric prompts.", ExpectedImpact = "Makes experience sound achievement-driven.", Difficulty = "Medium" });
        plan.Add(new ActionPlanItem { Priority = priority, Action = "Generate a tailored version for every high-value job.", ExpectedImpact = "Improves keyword alignment and application strategy.", Difficulty = "Low" });
        return plan;
    }

    private static string BuildExecutiveSummary(ResumeAnalysisInput input, ResumeAnalysisResult result)
    {
        string readiness = result.Scores.Overall >= 80 ? "job-ready with targeted polish" : result.Scores.Overall >= 65 ? "competitive but needs targeted strengthening" : "not yet strong enough for premium applications";
        string weakness = result.RecruiterRisks.FirstOrDefault()?.Finding ?? "Improve clarity and evidence density.";
        string opportunity = result.SkillGaps.FirstOrDefault(gap => gap.GapSeverity != "Covered")?.RequiredSkill ?? result.Skills.FirstOrDefault()?.Skill ?? "role-specific positioning";
        return "Current strength: " + readiness + "." + Environment.NewLine +
            "Main weakness: " + weakness + "." + Environment.NewLine +
            "Best opportunity: strengthen " + opportunity + " with truthful evidence and recruiter-readable bullets." + Environment.NewLine +
            "Target: " + DisplayOrFallback(input.TargetRole, "No target role entered") + ".";
    }

    private static string BuildJobMatchSummary(ResumeAnalysisInput input, ResumeAnalysisResult result)
    {
        if (string.IsNullOrWhiteSpace(input.JobDescription))
            return "Add a job description to generate a job-specific match analysis.";

        var critical = result.SkillGaps.Where(gap => gap.GapSeverity == "Critical").Take(5).Select(gap => gap.RequiredSkill).ToList();
        var covered = result.SkillGaps.Where(gap => gap.GapSeverity == "Covered").Take(6).Select(gap => gap.RequiredSkill).ToList();
        return "Match score: " + result.Scores.JobMatch + "/100." + Environment.NewLine +
            "Strong fit signals: " + (covered.Count == 0 ? "Add clearer evidence for required skills." : string.Join(", ", covered)) + "." + Environment.NewLine +
            "Potential rejection reasons: " + (critical.Count == 0 ? "No critical missing keywords detected." : string.Join(", ", critical)) + "." + Environment.NewLine +
            "Application strategy: tailor the summary, skills section, and top bullets to mirror the job language without inventing credentials.";
    }

    private static string ImproveBullet(string bullet, string targetRole)
    {
        string cleaned = Regex.Replace(bullet.Trim(), @"^(responsible for|helped|worked on|assisted with)\s+", "", RegexOptions.IgnoreCase);
        string verb = StrongVerbs[(cleaned.GetHashCode() & 0x7fffffff) % StrongVerbs.Length];
        if (Regex.IsMatch(cleaned, @"^(led|built|improved|reduced|delivered|automated|launched|optimized|designed|implemented)\b", RegexOptions.IgnoreCase))
            verb = char.ToUpperInvariant(cleaned[0]) + cleaned[1..].Split(' ', 2)[0].ToLowerInvariant();
        string roleContext = string.IsNullOrWhiteSpace(targetRole) ? "business or technical" : targetRole;
        string withoutVerb = Regex.Replace(cleaned, @"^[A-Za-z]+(ed|d)?\s+", "", RegexOptions.IgnoreCase).Trim();
        return verb + " " + withoutVerb + " to improve " + roleContext + " outcomes by [insert verified metric].";
    }

    private static string BuildSummarySentence(ResumeAnalysisInput input, ResumeAnalysisResult analysis)
    {
        string role = DisplayOrFallback(input.TargetRole, "professional role");
        string topSkills = analysis.Skills.Count == 0
            ? "cross-functional execution, problem solving, and stakeholder communication"
            : string.Join(", ", analysis.Skills.Take(5).Select(skill => skill.Skill));
        return "Results-focused " + role + " candidate with evidence across " + topSkills + ". Strongest positioning opportunity is to connect recent work to measurable outcomes, role-critical keywords, and recruiter-ready achievement bullets.";
    }

    private static string ExtractExperienceBlock(string resume)
    {
        var bullets = resume.SplitLines()
            .Select(line => line.Trim())
            .Where(line => line.Length > 12)
            .Take(18)
            .ToList();
        if (bullets.Count == 0)
            return "[Add role, company, dates, and 3 to 5 achievement bullets per recent role.]";
        return string.Join(Environment.NewLine, bullets.Select(line => line.StartsWith("-") ? line : "- " + line).Take(10));
    }

    private static string ExtractEducationBlock(string resume)
    {
        var lines = resume.SplitLines()
            .Where(line => ContainsAny(line.ToLowerInvariant(), "education", "degree", "university", "college", "certification", "certified"))
            .Take(8)
            .ToList();
        return lines.Count == 0 ? "[Add truthful education, certifications, licenses, and relevant training.]" : string.Join(Environment.NewLine, lines);
    }

    private static List<string> ExtractImportantTerms(string text)
    {
        string[] stop =
        {
            "and", "the", "with", "for", "you", "your", "our", "this", "that", "from", "will", "are", "have", "has", "job",
            "role", "team", "work", "experience", "candidate", "ability", "skills", "including", "within", "using", "about"
        };
        var stopSet = new HashSet<string>(stop, StringComparer.OrdinalIgnoreCase);
        var phrases = Regex.Matches(text.ToLowerInvariant(), @"[a-z][a-z0-9+#./-]*(?:\s+[a-z][a-z0-9+#./-]*){0,2}")
            .Select(match => match.Value.Trim())
            .Where(value => value.Length >= 3)
            .Where(value => !stopSet.Contains(value))
            .GroupBy(value => value)
            .Select(group => new { Term = group.Key, Count = group.Count(), Weight = group.Key.Contains(' ') ? 2 : 1 })
            .OrderByDescending(item => item.Count * item.Weight)
            .ThenBy(item => item.Term)
            .Select(item => TitleCaseKnownTerm(item.Term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return phrases;
    }

    private static bool IsLikelyCritical(string term)
    {
        string lower = term.ToLowerInvariant();
        return SkillCatalog.Values.SelectMany(value => value).Any(skill => lower.Contains(skill, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountMatches(string lower, IEnumerable<string> needles)
    {
        return needles.Count(needle => lower.Contains(needle.ToLowerInvariant()));
    }

    private static int CountOccurrences(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return 0;
        return Regex.Matches(text, Regex.Escape(term), RegexOptions.IgnoreCase).Count;
    }

    private static int CountLongLines(string text)
    {
        return text.SplitLines().Count(line => line.Length > 160);
    }

    private static List<string> Tokenize(string text)
    {
        return Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9+#.-]{2,}")
            .Select(match => match.Value)
            .ToList();
    }

    private static string DetectLocation(List<string> topLines)
    {
        return topLines.FirstOrDefault(line =>
            Regex.IsMatch(line, @"\b[A-Z][a-z]+,\s?[A-Z]{2}\b") ||
            ContainsAny(line.ToLowerInvariant(), "remote", "united states", "usa", "uk", "canada")) ?? "";
    }

    private static string EstimateYearsExperience(string resume)
    {
        var years = Regex.Matches(resume, @"\b(19|20)\d{2}\b")
            .Select(match => int.TryParse(match.Value, out int year) ? year : 0)
            .Where(year => year is >= 1980 and <= 2100)
            .ToList();
        if (years.Count < 2)
            return "Not enough dates to estimate.";
        int span = Math.Max(0, years.Max() - years.Min());
        return span == 0 ? "Less than 1 year visible." : span + "+ years visible from dates.";
    }

    private static void AddIf(List<ResumeFinding> items, bool condition, string section, string finding, string severity, string why, string fix)
    {
        if (!condition)
            return;
        items.Add(new ResumeFinding
        {
            Section = section,
            Finding = finding,
            Severity = severity,
            WhyItMatters = why,
            RecommendedFix = fix
        });
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static int ClampScore(int value)
    {
        return Math.Clamp(value, 0, 100);
    }

    private static string DisplayOrFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string TitleCaseKnownTerm(string term)
    {
        string lower = term.ToLowerInvariant();
        if (lower is "c#" or ".net" or "api" or "sql" or "aws" or "gcp" or "ai" or "ci/cd")
            return lower.ToUpperInvariant().Replace(".NET", ".NET");
        return string.Join(" ", lower.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Length <= 2 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..]));
    }
}

internal sealed class JobIntelligenceService
{
    private readonly ResumeAnalysisService _resumeService = new();

    public JobMatchReport AnalyzeJobMatch(ResumeAnalysisInput resumeInput, SavedJob job)
    {
        var analysis = _resumeService.Analyze(new ResumeAnalysisInput
        {
            ResumeText = resumeInput.ResumeText,
            TargetRole = string.IsNullOrWhiteSpace(job.Title) ? resumeInput.TargetRole : job.Title,
            TargetIndustry = resumeInput.TargetIndustry,
            SeniorityLevel = resumeInput.SeniorityLevel,
            ResumeType = resumeInput.ResumeType,
            JobDescription = job.JobDescription,
            CompanyName = job.Company,
            JobUrl = job.JobUrl,
            OutputStyle = resumeInput.OutputStyle
        });

        var report = new JobMatchReport
        {
            OverallFitScore = analysis.Scores.JobMatch > 0 ? analysis.Scores.JobMatch : analysis.Scores.Overall,
            SkillsMatch = analysis.Scores.KeywordAlignment,
            ExperienceMatch = analysis.Scores.Impact,
            KeywordMatch = analysis.Scores.KeywordAlignment,
            SeniorityMatch = EstimateSeniorityMatch(resumeInput, job),
            DomainMatch = EstimateDomainMatch(resumeInput, job),
            SkillGaps = analysis.SkillGaps,
            RequirementFindings = analysis.ExperienceFindings,
            Risks = analysis.RecruiterRisks,
            Strategy = BuildApplicationStrategy(analysis, job)
        };
        return report;
    }

    public string GenerateCoverLetter(ResumeAnalysisInput input, SavedJob job, JobMatchReport? report)
    {
        string title = string.IsNullOrWhiteSpace(job.Title) ? "the role" : job.Title;
        string company = string.IsNullOrWhiteSpace(job.Company) ? "your team" : job.Company;
        var strengths = report?.SkillGaps.Where(gap => gap.GapSeverity == "Covered").Take(3).Select(gap => gap.RequiredSkill).ToList() ?? new List<string>();
        if (strengths.Count == 0)
            strengths.AddRange(new[] { "relevant execution", "problem solving", "measurable contribution" });

        return "Dear Hiring Team,\r\n\r\n" +
            "I am excited to apply for " + title + " at " + company + ". My background aligns most strongly with " + string.Join(", ", strengths) + ", and I am especially interested in contributing where the role needs practical execution, clear communication, and measurable results.\r\n\r\n" +
            "Across my experience, I have built a habit of turning ambiguous goals into organized work, improving processes, and partnering with stakeholders to deliver outcomes. For this role, I would emphasize the projects and achievements in my resume that map directly to your requirements, while being transparent about any gaps I am actively closing.\r\n\r\n" +
            "I would welcome the chance to discuss how my experience can support " + company + "'s goals.\r\n\r\n" +
            "Sincerely,\r\n" +
            "[Your Name]";
    }

    public string GenerateInterviewPrep(ResumeAnalysisInput input, SavedJob job, JobMatchReport? report)
    {
        var gaps = report?.SkillGaps.Where(g => g.GapSeverity is "Critical" or "Important").Take(5).ToList() ?? new List<SkillGapFinding>();
        var builder = new StringBuilder();
        builder.AppendLine("INTERVIEW PREP FOR " + (string.IsNullOrWhiteSpace(job.Title) ? "SELECTED JOB" : job.Title.ToUpperInvariant()));
        builder.AppendLine();
        builder.AppendLine("Recruiter Questions");
        builder.AppendLine("- Walk me through your background and why this role fits.");
        builder.AppendLine("- What compensation range are you targeting?");
        builder.AppendLine("- Why are you interested in " + Display(job.Company, "this company") + "?");
        builder.AppendLine();
        builder.AppendLine("Hiring Manager Questions");
        builder.AppendLine("- Tell me about a project that proves you can succeed in this role.");
        builder.AppendLine("- What would you prioritize in your first 30 days?");
        builder.AppendLine("- How do you handle ambiguity, tradeoffs, and stakeholder pressure?");
        builder.AppendLine();
        builder.AppendLine("Technical / Role-Specific Questions");
        foreach (var gap in (report?.SkillGaps.Take(6) ?? Enumerable.Empty<SkillGapFinding>()))
            builder.AppendLine("- How have you used or learned " + gap.RequiredSkill + "?");
        builder.AppendLine();
        builder.AppendLine("STAR Answer Themes");
        builder.AppendLine("- Situation: choose a recent project with business pressure.");
        builder.AppendLine("- Task: explain your ownership and constraint.");
        builder.AppendLine("- Action: show the decisions, collaboration, and tools used.");
        builder.AppendLine("- Result: give a verified metric or bracketed metric prompt.");
        builder.AppendLine();
        builder.AppendLine("Gaps To Prepare For");
        foreach (var gap in gaps)
            builder.AppendLine("- " + gap.RequiredSkill + ": prepare an honest bridge story or learning plan.");
        builder.AppendLine();
        builder.AppendLine("Questions To Ask Employer");
        builder.AppendLine("- What outcomes would make the first 90 days successful?");
        builder.AppendLine("- Which team or customer problems are most urgent?");
        builder.AppendLine("- What separates strong performers in this role?");
        return builder.ToString().Trim();
    }

    private static int EstimateSeniorityMatch(ResumeAnalysisInput input, SavedJob job)
    {
        string combined = (input.ResumeText + " " + job.JobDescription + " " + job.Title).ToLowerInvariant();
        int score = 62;
        if (combined.Contains("senior") || combined.Contains("lead") || combined.Contains("manager"))
            score += input.SeniorityLevel is "Senior" or "Lead" or "Manager" or "Executive" ? 20 : -8;
        if (combined.Contains("entry") || combined.Contains("junior"))
            score += input.SeniorityLevel == "Entry" ? 20 : -5;
        return Math.Clamp(score, 0, 100);
    }

    private static int EstimateDomainMatch(ResumeAnalysisInput input, SavedJob job)
    {
        if (string.IsNullOrWhiteSpace(input.TargetIndustry))
            return 60;
        return (job.JobDescription + " " + job.Company).Contains(input.TargetIndustry, StringComparison.OrdinalIgnoreCase) ? 84 : 58;
    }

    private static string BuildApplicationStrategy(ResumeAnalysisResult analysis, SavedJob job)
    {
        var missing = analysis.SkillGaps.Where(gap => gap.GapSeverity is "Critical" or "Important").Take(4).Select(gap => gap.RequiredSkill).ToList();
        return "Use a tailored resume for " + Display(job.Title, "this job") + ". Lead with the closest matching qualifications, add job-language keywords naturally, and prepare honest answers for " +
            (missing.Count == 0 ? "any role-specific depth questions." : string.Join(", ", missing) + ".");
    }

    private static string Display(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

internal sealed class CareerStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string RootFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoleAxis");
    public string ResumeFolder => Path.Combine(RootFolder, "Resume");
    public string JobsFolder => Path.Combine(RootFolder, "Jobs");
    public string SavedJobsPath => Path.Combine(JobsFolder, "saved_jobs.json");

    public CareerStorageService()
    {
        Directory.CreateDirectory(Path.Combine(ResumeFolder, "analyses"));
        Directory.CreateDirectory(Path.Combine(ResumeFolder, "generated_resumes"));
        Directory.CreateDirectory(Path.Combine(ResumeFolder, "reports"));
        Directory.CreateDirectory(Path.Combine(JobsFolder, "cover_letters"));
        Directory.CreateDirectory(Path.Combine(JobsFolder, "interview_contexts"));
        Directory.CreateDirectory(Path.Combine(JobsFolder, "tailored_resumes"));
    }

    public List<SavedJob> LoadJobs()
    {
        try
        {
            if (!File.Exists(SavedJobsPath))
                return new List<SavedJob>();
            return JsonSerializer.Deserialize<List<SavedJob>>(File.ReadAllText(SavedJobsPath)) ?? new List<SavedJob>();
        }
        catch
        {
            return new List<SavedJob>();
        }
    }

    public void SaveJobs(IEnumerable<SavedJob> jobs)
    {
        Directory.CreateDirectory(JobsFolder);
        File.WriteAllText(SavedJobsPath, JsonSerializer.Serialize(jobs.OrderByDescending(job => job.DateSaved).ToList(), JsonOptions));
    }

    public string SaveResumeReport(ResumeAnalysisResult result)
    {
        string path = Path.Combine(ResumeFolder, "reports", "resume_report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
        return path;
    }

    public string SaveResumeText(string text, string prefix = "upgraded_resume")
    {
        string path = Path.Combine(ResumeFolder, "generated_resumes", prefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        File.WriteAllText(path, text ?? "");
        return path;
    }

    public string SaveCoverLetter(Guid jobId, string text)
    {
        string path = Path.Combine(JobsFolder, "cover_letters", "cover_letter_" + jobId.ToString("N")[..8] + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        File.WriteAllText(path, text ?? "");
        return path;
    }

    public string SaveTailoredResume(Guid jobId, string text)
    {
        string path = Path.Combine(JobsFolder, "tailored_resumes", "tailored_resume_" + jobId.ToString("N")[..8] + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        File.WriteAllText(path, text ?? "");
        return path;
    }

    public string SaveInterviewContext(SavedJob job, string resumeSummary, string interviewPrep)
    {
        string path = Path.Combine(JobsFolder, "interview_contexts", "roleaxis_interview_context_" + job.Id.ToString("N")[..8] + ".json");
        var payload = new
        {
            job_id = job.Id,
            job_title = job.Title,
            company = job.Company,
            job_description = job.JobDescription,
            resume_summary = resumeSummary,
            generated_questions_and_answers = interviewPrep,
            created_at = DateTime.Now
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
        return path;
    }

    public string SaveAnalysisCsv(ResumeAnalysisResult result)
    {
        string path = Path.Combine(ResumeFolder, "analyses", "resume_analysis_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
        var builder = new StringBuilder();
        builder.AppendLine("section,finding,severity,recommended_fix");
        foreach (var item in result.AtsFindings.Concat(result.ExperienceFindings).Concat(result.RecruiterRisks))
            builder.AppendLine(Csv(item.Section) + "," + Csv(item.Finding) + "," + Csv(item.Severity) + "," + Csv(item.RecommendedFix));
        File.WriteAllText(path, builder.ToString());
        return path;
    }

    private static string Csv(string value)
    {
        return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}

internal static class GptPromptService
{
    public static string BuildResumeAnalysisPrompt(ResumeAnalysisInput input)
    {
        return "You are RoleAxis Resume Intelligence. Return structured sections: Score Summary, Resume Metadata, ATS Findings, Skills, Skill Gaps, Experience Findings, Weak Bullets, Keyword Analysis, Recruiter Risks, Recommendation Plan.\n" +
            "Be strict, recruiter-grade, and honest. Do not invent credentials, employers, dates, metrics, or achievements.\n\n" +
            "Resume:\n" + input.ResumeText + "\n\nTarget Role:\n" + input.TargetRole + "\n\nJob Description:\n" + input.JobDescription;
    }

    public static string BuildResumeRewritePrompt(ResumeAnalysisInput input)
    {
        return "You are RoleAxis Resume Intelligence. Use this multi-pass workflow internally before final output:\n" +
            "Pass 1: analyze original resume and target role.\n" +
            "Pass 2: identify weak sections and missing job keywords.\n" +
            "Pass 3: rewrite with stronger positioning, metrics, and role alignment.\n" +
            "Pass 4: self-review for ATS compatibility, clarity, keyword alignment, achievement strength, honesty, recruiter readability, and formatting quality.\n" +
            "Pass 5: revise again before final output.\n\n" +
            "Do not fabricate degrees, employers, certifications, job titles, dates, numbers, or achievements. Use bracketed prompts for missing metrics.\n\n" +
            "Final output sections: Final Upgraded Resume, Change Summary, Why This Version Is Stronger, Keywords Added, Remaining Gaps, Interview Talking Points From Resume.\n\n" +
            "Resume:\n" + input.ResumeText + "\n\nTarget role:\n" + input.TargetRole + "\n\nJob description:\n" + input.JobDescription;
    }

    public static string BuildJobMatchPrompt(ResumeAnalysisInput input, SavedJob job)
    {
        return "You are RoleAxis Job Intelligence. Compare the resume against the job. Return match score, missing skills, evidence from resume, rejection risks, and application strategy. Do not fake experience.\n\n" +
            "Resume:\n" + input.ResumeText + "\n\nJob:\n" + job.Title + " at " + job.Company + "\n\nJob Description:\n" + job.JobDescription;
    }

    public static string BuildCoverLetterPrompt(ResumeAnalysisInput input, SavedJob job)
    {
        return "Write a concise, role-specific cover letter under 250 words. Use resume evidence and job requirements. Avoid generic filler and do not invent achievements.\n\n" +
            "Resume:\n" + input.ResumeText + "\n\nJob:\n" + job.Title + " at " + job.Company + "\n\nJob Description:\n" + job.JobDescription;
    }

    public static string BuildInterviewPrepPrompt(ResumeAnalysisInput input, SavedJob job)
    {
        return "Generate interview prep by category: recruiter, hiring manager, technical, behavioral, STAR answer suggestions, questions to ask employer, and red flags. Use resume evidence and job requirements.\n\n" +
            "Resume:\n" + input.ResumeText + "\n\nJob:\n" + job.Title + " at " + job.Company + "\n\nJob Description:\n" + job.JobDescription;
    }
}
