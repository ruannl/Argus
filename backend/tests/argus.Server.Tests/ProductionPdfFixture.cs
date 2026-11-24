using System.Globalization;

namespace argus.Server.Tests;

public enum ProductionAssetVariant
{
    Valid,
    Missing,
    DifferentBytes,
    Relocated,
    WrongFilter,
    WrongDimensions,
    Rotated,
    Masked,
}

public enum ProductionCanary
{
    None,
    Title,
    Notice,
    Footer,
    AccountRight,
    Available,
    Payments,
}

public enum ProductionFlowVariant
{
    Valid,
    CrossBoundary,
    Misaligned,
}

public static class ProductionPdfFixture
{
    public const string PrivacyHolder = "CANARY HOLDER 4471";
    public const string PrivacyStreet = "1 CANARY STREET";
    public const string PrivacySuburb = "CANARY SUBURB";
    public const string PrivacyProduct = "CANARY PRODUCT";
    public const string PrivacyCity = "CANARY";
    public const string PrivacyOpeningBalance = "2,345.67";
    public const string PrivacyAvailableBalance = "3,456.78";
    public const string PrivacyPaymentsTotal = "4,567.89";
    public const string PrivacyDepositsTotal = "5,678.90";
    public const string PrivacyBalance = "8,765.43";

    private const double HeaderFirstCenterY = 535.29;
    private const double HeaderLaterCenterY = 605.35;
    private const double RowFirstCenterY = 475.98;
    private const double RowLaterCenterY = 577.21;
    private const double RowPitch = 31.5;
    private const double DescriptionOffset = 8.5;
    private const double AssetLeft = 18.43;
    private const double AssetBottom = 714.31;
    private const double AssetRight = 175.82;
    private const double AssetTop = 768.31;
    private const int AssetWidth = 656;
    private const int AssetHeight = 225;
    private const string AssetFilter = "FlateDecode";

    public static byte[] Build(int pageCount,
                               string flowMode = "valid",
                               ProductionAssetVariant asset = ProductionAssetVariant.Valid,
                               bool includeText = true,
                               string balance = "1,000.00",
                               bool privacyMetadata = false,
                               bool conflictingHeader = false,
                               ProductionCanary canary = ProductionCanary.None,
                               ProductionFlowVariant flowVariant = ProductionFlowVariant.Valid,
                               bool conflictingPeriod = false)
    {
        if (pageCount is not 4 and not 18)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount));
        }

        string openingBalance = privacyMetadata ? PrivacyOpeningBalance : "1,000.00";
        string availableBalance = privacyMetadata ? PrivacyAvailableBalance : "1,000.00";
        string paymentsTotal = privacyMetadata ? PrivacyPaymentsTotal : "1,000.00";
        string depositsTotal = privacyMetadata ? PrivacyDepositsTotal : "1,000.00";
        int[] counts = RowCounts(pageCount);
        byte[]? assetBytes = asset == ProductionAssetVariant.Missing ? null : LoadAssetBytes();
        MinimalPdfBuilder builder = new();
        int rowIndex = 0;
        for (int pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            List<PdfText> texts = [];
            if (includeText)
            {
                AddMasthead(texts, pageNumber == 1, canary, conflictingPeriod);
                if (pageNumber == 1)
                {
                    AddFirstPageMetadata(texts, privacyMetadata);
                    AddOpeningBalance(texts, openingBalance);
                }
                else
                {
                    AddLaterPageMetadata(texts, canary);
                }

                AddLeadIn(texts, pageNumber == 1 ? 558.59 : 628.65, availableBalance, canary);
                AddHeader(texts, pageNumber == 1 ? HeaderFirstCenterY : HeaderLaterCenterY);
                double rowCenter = pageNumber == 1 ? RowFirstCenterY : RowLaterCenterY;
                int rows = counts[pageNumber - 1];
                for (int row = 0; row < rows; row++)
                {
                    AddRow(texts, rowIndex, rowCenter, flowMode, balance, flowVariant);
                    rowIndex++;
                    rowCenter -= RowPitch;
                }

                if (pageNumber == pageCount)
                {
                    AddPostamble(texts, rowCenter + RowPitch, paymentsTotal, depositsTotal, canary);
                }

                if (conflictingHeader && pageNumber == 1)
                {
                    AddConflictingHeader(texts);
                }

                AddFooter(texts, pageNumber, pageCount, canary);
            }

            if (assetBytes is null)
            {
                builder.AddPage(texts, 612, 792);
            }
            else
            {
                builder.AddPage(texts, MakeAsset(asset, assetBytes), 612, 792);
            }
        }

        return builder.Build();
    }

    public static byte[] BuildManyPages(int pageCount)
    {
        MinimalPdfBuilder builder = new();
        for (int index = 0; index < pageCount; index++)
        {
            builder.AddPage([new PdfText(40, 700, "Synthetic")], 612, 792);
        }

        return builder.Build();
    }

    public static byte[] BuildOversizeCharacters(int characters)
    {
        byte[] asset = LoadAssetBytes();
        PdfFixtureImage image = new(asset, AssetWidth, AssetHeight, AssetFilter, AssetLeft, AssetBottom, AssetRight, AssetTop);
        MinimalPdfBuilder builder = new();
        builder.AddPage([new PdfText(40, 700, new string('x', characters))], image, 612, 792);

        return builder.Build();
    }

    private static void AddMasthead(List<PdfText> texts, bool firstPage, ProductionCanary canary, bool conflictingPeriod)
    {
        AddLine(texts, 477.8, 755.26, "Customer Care: 000 000 0000");
        AddLine(texts, 462.7, 743.85, "Website: www.standardbank.co.za");
        if (firstPage)
        {
            AddLine(texts, 279.1, 727.11, canary == ProductionCanary.Title ? "STANDARD BANK 4 month statement X" : "STANDARD BANK 4 month statement");
        }
        else
        {
            AddLine(texts, 213, 729.08, "STANDARD BANK");
            AddLine(texts, 487.7, 726.32, "4 month statement");
        }

        AddLine(texts, 525.7, 712.48, !firstPage && conflictingPeriod ? "From: 01 Feb 25" : "From: 01 Jan 25");
        AddLine(texts, 289.9, firstPage ? 705.82 : 706.61, "SYNTHETIC BRANCH");
        AddLine(texts, 536.8, 696.86, !firstPage && conflictingPeriod ? "To: 28 Feb 25" : "To: 31 Jan 25");
        AddLine(texts, 295.2, firstPage ? 683.39 : 684.18, "15 February 2025");
    }

    private static void AddFirstPageMetadata(List<PdfText> texts, bool privacyMetadata)
    {
        string street = privacyMetadata ? PrivacyStreet : "1 SYNTHETIC STREET";
        string holder = privacyMetadata ? PrivacyHolder : "SYNTHETIC HOLDER";
        string suburb = privacyMetadata ? PrivacySuburb : "SYNTHETIC SUBURB";
        string product = privacyMetadata ? PrivacyProduct : "CHEQUE ACCOUNT";
        string city = privacyMetadata ? PrivacyCity : "CITY";
        double cityX = privacyMetadata ? 540 : 563.2;
        AddLine(texts, 549.9, 665.16, "Address:");
        AddLine(texts, 314.1, 661.52, "1");
        AddLine(texts, 32.6, 649.18, "Account Number: 0000000000");
        AddLine(texts, 450, 649.18, street);
        AddLine(texts, 32.6, 633.57, $"Account Holder: {holder}");
        AddLine(texts, 450, 633.57, suburb);
        AddLine(texts, 32.6, 617.97, $"Product Name: {product}");
        AddLine(texts, 450, 617.97, "0000");
        AddLine(texts, cityX, 601.96, city);
        AddLine(texts, 566, 586.28, "0000");
    }

    private static void AddLaterPageMetadata(List<PdfText> texts, ProductionCanary canary)
    {
        AddLine(texts, 32.6, 665.03, "Account Number: 0000000000");
        AddLine(texts, 248, 662.31, canary == ProductionCanary.AccountRight ? "1 X" : "1");
    }

    private static void AddConflictingHeader(List<PdfText> texts)
    {
        AddLine(texts, 38.3, 400, "Date");
        AddLine(texts, 95, 400, "Description");
        AddLine(texts, 220, 400, "Payments");
        AddLine(texts, 300, 400, "Deposits");
        AddLine(texts, 400, 400, "Bank");
        AddLine(texts, 440, 400, "fees");
        AddLine(texts, 500, 400, "Balance");
    }

    private static void AddLeadIn(List<PdfText> texts, double centerY, string availableBalance, ProductionCanary canary)
    {
        AddLine(texts, 32.6, centerY, "Transaction details");
        AddLine(texts, 330, centerY, canary == ProductionCanary.Available
            ? $"Available Balance: R {availableBalance} X"
            : $"Available Balance: R {availableBalance}");
    }

    private static void AddHeader(List<PdfText> texts, double centerY)
    {
        AddLine(texts, 38.3, centerY, "Date");
        AddLine(texts, 95, centerY, "Description");
        AddLine(texts, 323.032, centerY, "Payments");
        AddLine(texts, 398.28, centerY, "Deposits");
        AddLine(texts, 462.848, centerY, "Bank");
        AddLine(texts, 485.08, centerY, "fees");
        AddLine(texts, 550.696, centerY, "Balance");
    }

    private static void AddOpeningBalance(List<PdfText> texts, string openingBalance)
    {
        AddLine(texts, 122.2, 507.36, "STATEMENT OPENING BALANCE");
        AddLine(texts, 550, 507.36, openingBalance);
    }

    private static void AddRow(List<PdfText> texts, int rowIndex, double centerY, string flowMode, string balance, ProductionFlowVariant flowVariant)
    {
        DateOnly date = new DateOnly(2025, 1, 1).AddDays(rowIndex % 28);
        AddLine(texts, 38.3, centerY, date.ToString("dd MMM yy", CultureInfo.InvariantCulture));
        string description = flowMode == "long"
            ? $"Synthetic row {rowIndex + 1} 12345678901234567890"
            : $"Synthetic row {rowIndex + 1}";
        AddLine(texts, 95, centerY - DescriptionOffset, description);

        if (flowVariant == ProductionFlowVariant.CrossBoundary)
        {
            AddLine(texts, 287, centerY, "10.00");
        }
        else if (flowVariant == ProductionFlowVariant.Misaligned)
        {
            AddLine(texts, 300, centerY, "100.00");
        }
        else
        {
            switch (flowMode)
            {
                case "valid":
                    AddAmount(texts, rowIndex % 3, centerY, $"{10 + rowIndex % 90}.00");
                    break;
                case "long":
                    AddAmount(texts, rowIndex % 3, centerY, $"{10 + rowIndex % 90}.00");
                    break;
                case "zero":
                    AddAmount(texts, 0, centerY, "0.00");
                    break;
                case "multiple":
                    AddAmount(texts, 0, centerY, "10.00");
                    AddAmount(texts, 1, centerY, "20.00");
                    break;
                case "invalid":
                    AddAmount(texts, 0, centerY, "garbage");
                    break;
                case "mixed":
                    if (rowIndex % 2 == 0)
                    {
                        AddAmount(texts, 0, centerY, "garbage");
                    }
                    else
                    {
                        AddAmount(texts, rowIndex % 3, centerY, $"{10 + rowIndex % 90}.00");
                    }
                    break;
                case "separate-sign":
                    AddLine(texts, 335, centerY, "-");
                    AddLine(texts, 338.6, centerY, "10.00");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(flowMode));
            }
        }

        AddLine(texts, 550, centerY, balance);
    }

    private static void AddAmount(List<PdfText> texts, int flowIndex, double centerY, string amount)
    {
        double edge = flowIndex switch
        {
            0 => 358.6,
            1 => 429.4,
            _ => 500.2,
        };
        double x = edge - amount.Length * 4.0;
        AddLine(texts, x, centerY, amount);
    }

    private static void AddPostamble(List<PdfText> texts,
                                     double lastRowCenterY,
                                     string paymentsTotal,
                                     string depositsTotal,
                                     ProductionCanary canary)
    {
        double notice = lastRowCenterY - 32.5;
        AddLine(texts, 32.6, notice, canary == ProductionCanary.Notice
            ? "Please verify all transactions reflected on this statement and notify any discrepancies to the bank as soon as possible. X"
            : "Please verify all transactions reflected on this statement and notify any discrepancies to the bank as soon as possible.");
        AddLine(texts, 41, notice - 48.8, "Statement Summary");
        AddLine(texts, 41, notice - 73.6, canary == ProductionCanary.Payments
            ? $"Payments - R {paymentsTotal} X"
            : $"Payments - R {paymentsTotal}");
        AddLine(texts, 41, notice - 100.8, $"Deposits R {depositsTotal}");
        AddLine(texts, 41, notice - 124.6, "Today's debits have not yet been paid");
    }

    private static void AddFooter(List<PdfText> texts, int pageNumber, int pageCount, ProductionCanary canary)
    {
        string legal = "The Standard Bank of South Africa Limited (Reg. No. 0000/000000/00. Authorised financial service provider. VAT Reg No. 0000000000 Registered credit provider (NCRCP00).";
        AddLine(
            texts,
            32.6,
            35.96,
            canary == ProductionCanary.Footer
                ? $"{legal} Pg {pageNumber} of {pageCount} X"
                : $"{legal} Pg {pageNumber} of {pageCount}",
            5);
        AddLine(
            texts,
            32.6,
            29.22,
            "We subscribe to the Code of Banking Association South Africa and, for unresolved disputes, support resolution through the Ombudsman for Banking Services",
            5);
    }

    private static void AddLine(List<PdfText> texts, double x, double centerY, string text, double fontSize = 8) =>
        texts.Add(new PdfText(x, centerY - fontSize * 0.36, text, fontSize));

    private static int[] RowCounts(int pageCount)
    {
        int[] counts = new int[pageCount];
        counts[0] = 14;
        for (int index = 1; index < pageCount - 1; index++)
        {
            counts[index] = 17;
        }
        counts[^1] = pageCount == 4 ? 1 : 11;

        return counts;
    }

    private static PdfFixtureImage MakeAsset(ProductionAssetVariant variant, byte[] bytes) =>
        variant switch
        {
            ProductionAssetVariant.DifferentBytes => new PdfFixtureImage(Alter(bytes), AssetWidth, AssetHeight, AssetFilter, AssetLeft, AssetBottom, AssetRight, AssetTop),
            ProductionAssetVariant.Relocated => new PdfFixtureImage(bytes, AssetWidth, AssetHeight, AssetFilter, AssetLeft + 20, AssetBottom + 20, AssetRight + 20, AssetTop + 20),
            ProductionAssetVariant.WrongFilter => new PdfFixtureImage(bytes, AssetWidth, AssetHeight, "DCTDecode", AssetLeft, AssetBottom, AssetRight, AssetTop),
            ProductionAssetVariant.WrongDimensions => new PdfFixtureImage(bytes, 600, 200, AssetFilter, AssetLeft, AssetBottom, AssetRight, AssetTop),
            ProductionAssetVariant.Rotated => new PdfFixtureImage(bytes, AssetWidth, AssetHeight, AssetFilter, AssetLeft, AssetBottom, AssetRight, AssetTop, RotationDegrees: 90),
            ProductionAssetVariant.Masked => new PdfFixtureImage(bytes, AssetWidth, AssetHeight, AssetFilter, AssetLeft, AssetBottom, AssetRight, AssetTop, IsMask: true),
            _ => new PdfFixtureImage(bytes, AssetWidth, AssetHeight, AssetFilter, AssetLeft, AssetBottom, AssetRight, AssetTop),
        };

    private static byte[] Alter(byte[] bytes)
    {
        byte[] copy = (byte[])bytes.Clone();
        copy[0] ^= 0xFF;

        return copy;
    }

    private static byte[] LoadAssetBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Statements", "synthetic-masthead.zlib"));
}
