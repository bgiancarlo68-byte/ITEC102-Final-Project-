using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

class Student
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime RegisteredAt { get; set; } = DateTime.Now;
}

class AttendanceRecord
{
    public string StudentId { get; set; } = "";
    public string Date { get; set; } = "";        // yyyy-MM-dd
    public string Subject { get; set; } = "";
    public string ClassTime { get; set; } = "";   // e.g. 08:00 (class start)
    public string LoginTime { get; set; } = "";   // actual login time, "--" if absent
    public string Status { get; set; } = "";      // Present / Late / Absent
}

class ClassSlot
{
    public string Subject { get; }
    public TimeSpan Start { get; }
    public TimeSpan End { get; }

    public ClassSlot(string subject, int sh, int sm, int eh, int em)
    {
        Subject = subject;
        Start = new TimeSpan(sh, sm, 0);
        End = new TimeSpan(eh, em, 0);
    }
}

class Program
{
    static List<Student> students = new();
    static List<AttendanceRecord> records = new();

    static readonly string studentFile = "students.json";
    static readonly string attendanceFile = "attendance.json";

    // ---------- SETTINGS ----------
    // Logging in within this many minutes after the class starts = Present.
    // After that (until the class ends) = Late.
    static readonly int GraceMinutes = 15;

    // Students may log in this many minutes BEFORE a class starts.
    static readonly int EarlyLoginMinutes = 10;

    // ---------- BSCS 1B WEEKLY SCHEDULE ----------
    static readonly Dictionary<DayOfWeek, List<ClassSlot>> schedule = new()
    {
        [DayOfWeek.Monday] = new()
        {
            new ClassSlot("ITEC 101", 8, 0, 9, 0),
            new ClassSlot("GEC 102", 10, 0, 12, 0),
            new ClassSlot("ITEC 102", 13, 0, 14, 0),
            new ClassSlot("PI 100", 15, 0, 16, 0)
        },
        [DayOfWeek.Tuesday] = new()
        {
            new ClassSlot("GEC 102", 10, 0, 11, 0),
            new ClassSlot("KOMFIL", 12, 0, 13, 0),
            new ClassSlot("GEC 101", 13, 0, 15, 0),
            new ClassSlot("ITEC 102", 15, 0, 16, 0)
        },
        [DayOfWeek.Wednesday] = new()
        {
            new ClassSlot("ITEC 101", 8, 0, 9, 0),
            new ClassSlot("GEC 101", 9, 0, 10, 0),
            new ClassSlot("PATHFIT", 10, 0, 12, 0),
            new ClassSlot("ITEC 102", 13, 0, 16, 0),
            new ClassSlot("ITEC 101", 16, 0, 17, 0)
        },
        [DayOfWeek.Thursday] = new()
        {
            new ClassSlot("KOMFIL", 10, 0, 12, 0),
            new ClassSlot("PI 100", 13, 0, 15, 0)
        }
    };

    static void Main()
    {
        students = LoadData<Student>(studentFile);
        records = LoadData<AttendanceRecord>(attendanceFile);

        // Old student entries without a registration time start counting from now
        foreach (Student s in students.Where(s => s.RegisteredAt == default))
        {
            s.RegisteredAt = DateTime.Now;
        }

        MarkAbsences();
        SaveAll();

        bool running = true;

        while (running)
        {
            Console.Clear();
            Console.WriteLine("======================================");
            Console.WriteLine(" STUDENT ATTENDANCE MONITORING SYSTEM");
            Console.WriteLine("              BSCS 1B");
            Console.WriteLine("======================================");
            Console.WriteLine($" {DateTime.Now:dddd, MMM dd yyyy  hh:mm:ss tt}");
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("1. Register Student");
            Console.WriteLine("2. View All Students");
            Console.WriteLine("3. Student Log In (Class Check-in)");
            Console.WriteLine("4. Search Student");
            Console.WriteLine("5. View Attendance Records");
            Console.WriteLine("6. Attendance Summary");
            Console.WriteLine("7. Export Attendance to CSV");
            Console.WriteLine("8. Export Full Report to PDF");
            Console.WriteLine("0. Exit");
            Console.WriteLine("--------------------------------------");
            Console.Write("Choose an option: ");

            string choice = Console.ReadLine() ?? "";

            // Automatically mark missed classes as Absent before every action
            MarkAbsences();

            switch (choice)
            {
                case "1": AddStudent(); break;
                case "2": ViewStudents(); break;
                case "3": StudentLogin(); break;
                case "4": SearchStudent(); break;
                case "5": ViewRecords(); break;
                case "6": ShowSummary(); break;
                case "7": ExportAttendance(); break;
                case "8": ExportFullReportPdf(); break;
                case "0":
                    SaveAll();
                    running = false;
                    Console.WriteLine("Data saved. Goodbye!");
                    break;
                default:
                    Console.WriteLine("Invalid choice. Try again.");
                    Pause();
                    break;
            }
        }
    }

    // =====================================================
    //  REGISTRATION
    // =====================================================
    static void AddStudent()
    {
        Console.Clear();
        Console.WriteLine("=== REGISTER STUDENT ===");

        Console.Write("Enter student ID: ");
        string id = (Console.ReadLine() ?? "").Trim();

        if (string.IsNullOrWhiteSpace(id))
        {
            Console.WriteLine("Student ID cannot be empty.");
            Pause();
            return;
        }

        if (students.Any(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Student ID already exists.");
            Pause();
            return;
        }

        Console.Write("Enter student name: ");
        string name = (Console.ReadLine() ?? "").Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Student name cannot be empty.");
            Pause();
            return;
        }

        students.Add(new Student
        {
            Id = id,
            Name = name,
            RegisteredAt = DateTime.Now
        });

        SaveAll();
        Console.WriteLine("Student registered successfully!");
        Console.WriteLine("Attendance will now be recorded automatically when you log in during class.");
        Pause();
    }

    static void ViewStudents()
    {
        Console.Clear();
        Console.WriteLine("=== REGISTERED STUDENTS ===");

        if (students.Count == 0)
        {
            Console.WriteLine("No students registered yet.");
        }
        else
        {
            Console.WriteLine("{0,-15} {1}", "STUDENT ID", "NAME");
            Console.WriteLine("--------------------------------------");

            foreach (Student s in students)
            {
                Console.WriteLine("{0,-15} {1}", s.Id, s.Name);
            }
        }

        Pause();
    }

    // =====================================================
    //  AUTOMATIC LOGIN / CHECK-IN (REAL TIME)
    // =====================================================
    static void StudentLogin()
    {
        Console.Clear();
        Console.WriteLine("=== STUDENT LOG IN ===");

        if (students.Count == 0)
        {
            Console.WriteLine("No students registered yet.");
            Pause();
            return;
        }

        Console.Write("Enter your student ID: ");
        string id = (Console.ReadLine() ?? "").Trim();

        Student? student = FindStudent(id);

        if (student == null)
        {
            Console.WriteLine("Student not found. Please register first.");
            Pause();
            return;
        }

        DateTime now = DateTime.Now;
        ClassSlot? slot = GetCurrentClass(now);

        Console.WriteLine($"\nWelcome, {student.Name}!");
        Console.WriteLine($"Log in time: {now:hh:mm:ss tt}");

        if (slot == null)
        {
            Console.WriteLine("\nNo class is open for log in right now.");
            ClassSlot? next = GetNextClass(now, out DateTime nextStart);
            if (next != null)
            {
                Console.WriteLine($"Next class: {next.Subject} on {nextStart:dddd} at {nextStart:h:mm tt}");
            }
            Pause();
            return;
        }

        DateTime classStart = now.Date + slot.Start;
        string date = now.ToString("yyyy-MM-dd");
        string classTime = classStart.ToString("HH:mm");

        AttendanceRecord? existing = records.FirstOrDefault(r =>
            r.StudentId.Equals(student.Id, StringComparison.OrdinalIgnoreCase)
            && r.Date == date
            && r.Subject == slot.Subject
            && r.ClassTime == classTime);

        if (existing != null)
        {
            Console.WriteLine($"\nYou already logged in for {slot.Subject} today.");
            Console.WriteLine($"Status: {existing.Status} (at {existing.LoginTime})");
            Pause();
            return;
        }

        // Status is decided ONLY by the system clock
        string status = now <= classStart.AddMinutes(GraceMinutes) ? "Present" : "Late";

        records.Add(new AttendanceRecord
        {
            StudentId = student.Id,
            Date = date,
            Subject = slot.Subject,
            ClassTime = classTime,
            LoginTime = now.ToString("hh:mm:ss tt"),
            Status = status
        });

        SaveAll();

        Console.WriteLine($"\nSubject: {slot.Subject} ({Fmt(slot.Start)} - {Fmt(slot.End)})");
        Console.WriteLine($"Status : {status.ToUpper()}");
        Pause();
    }

    // Returns the class the student can log in to right now (or null)
    static ClassSlot? GetCurrentClass(DateTime now)
    {
        if (!schedule.TryGetValue(now.DayOfWeek, out List<ClassSlot>? slots))
        {
            return null;
        }

        TimeSpan t = now.TimeOfDay;

        // 1) A class that is currently ongoing
        ClassSlot? ongoing = slots.FirstOrDefault(s => t >= s.Start && t < s.End);
        if (ongoing != null)
        {
            return ongoing;
        }

        // 2) A class about to start (early login window)
        return slots.FirstOrDefault(s =>
            t < s.Start && s.Start - t <= TimeSpan.FromMinutes(EarlyLoginMinutes));
    }

    static ClassSlot? GetNextClass(DateTime now, out DateTime start)
    {
        for (int i = 0; i <= 7; i++)
        {
            DateTime day = now.Date.AddDays(i);

            if (!schedule.TryGetValue(day.DayOfWeek, out List<ClassSlot>? slots))
            {
                continue;
            }

            foreach (ClassSlot s in slots.OrderBy(s => s.Start))
            {
                if (day + s.Start > now)
                {
                    start = day + s.Start;
                    return s;
                }
            }
        }

        start = default;
        return null;
    }

    // =====================================================
    //  AUTOMATIC ABSENCES
    //  Any class that already ended with no log in = Absent
    // =====================================================
    static void MarkAbsences()
    {
        DateTime now = DateTime.Now;
        bool changed = false;

        foreach (Student student in students)
        {
            for (DateTime day = student.RegisteredAt.Date; day <= now.Date; day = day.AddDays(1))
            {
                if (!schedule.TryGetValue(day.DayOfWeek, out List<ClassSlot>? slots))
                {
                    continue;
                }

                foreach (ClassSlot slot in slots)
                {
                    DateTime start = day + slot.Start;
                    DateTime end = day + slot.End;

                    // Class not finished yet, or started before the student registered
                    if (end > now || start < student.RegisteredAt)
                    {
                        continue;
                    }

                    string date = day.ToString("yyyy-MM-dd");
                    string classTime = start.ToString("HH:mm");

                    bool hasRecord = records.Any(r =>
                        r.StudentId.Equals(student.Id, StringComparison.OrdinalIgnoreCase)
                        && r.Date == date
                        && r.Subject == slot.Subject
                        && r.ClassTime == classTime);

                    if (!hasRecord)
                    {
                        records.Add(new AttendanceRecord
                        {
                            StudentId = student.Id,
                            Date = date,
                            Subject = slot.Subject,
                            ClassTime = classTime,
                            LoginTime = "--",
                            Status = "Absent"
                        });
                        changed = true;
                    }
                }
            }
        }

        if (changed)
        {
            SaveAll();
        }
    }

    // =====================================================
    //  VIEWING
    // =====================================================
    static void SearchStudent()
    {
        Console.Clear();
        Console.WriteLine("=== SEARCH STUDENT ===");

        Console.Write("Enter student ID: ");
        string id = (Console.ReadLine() ?? "").Trim();

        Student? student = FindStudent(id);

        if (student == null)
        {
            Console.WriteLine("Student not found.");
            Pause();
            return;
        }

        Console.WriteLine($"Student ID: {student.Id}");
        Console.WriteLine($"Student Name: {student.Name}");
        Console.WriteLine("\nAttendance History:");

        var history = records
            .Where(r => r.StudentId.Equals(student.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Date)
            .ThenBy(r => r.ClassTime)
            .ToList();

        if (history.Count == 0)
        {
            Console.WriteLine("No attendance records found.");
        }
        else
        {
            Console.WriteLine("{0,-12} {1,-10} {2,-12} {3,-8}", "DATE", "SUBJECT", "LOGIN", "STATUS");
            Console.WriteLine("--------------------------------------");

            foreach (AttendanceRecord r in history)
            {
                Console.WriteLine("{0,-12} {1,-10} {2,-12} {3,-8}",
                    r.Date, r.Subject, r.LoginTime, r.Status);
            }
        }

        Pause();
    }

    static void ViewRecords()
    {
        Console.Clear();
        Console.WriteLine("=== ATTENDANCE RECORDS ===");

        if (records.Count == 0)
        {
            Console.WriteLine("No attendance records yet.");
        }
        else
        {
            foreach (AttendanceRecord r in records.OrderBy(r => r.Date).ThenBy(r => r.ClassTime))
            {
                Console.WriteLine(
                    $"{r.Date} | {r.Subject,-8} | {r.StudentId} | {GetName(r.StudentId)} | {r.LoginTime} | {r.Status}");
            }
        }

        Pause();
    }

    static void ShowSummary()
    {
        Console.Clear();
        Console.WriteLine("=== ATTENDANCE SUMMARY ===");

        int present = records.Count(r => r.Status == "Present");
        int absent = records.Count(r => r.Status == "Absent");
        int late = records.Count(r => r.Status == "Late");

        Console.WriteLine($"Registered Students: {students.Count}");
        Console.WriteLine($"Total Records: {records.Count}");
        Console.WriteLine($"Present: {present}");
        Console.WriteLine($"Late: {late}");
        Console.WriteLine($"Absent: {absent}");

        if (records.Count > 0)
        {
            double rate = (double)present / records.Count * 100;
            Console.WriteLine($"Present Rate: {rate:F2}%");

            Console.WriteLine("\nPer Student:");
            Console.WriteLine("{0,-12} {1,-20} {2,-8} {3,-6} {4,-7}", "ID", "NAME", "PRESENT", "LATE", "ABSENT");
            Console.WriteLine("-------------------------------------------------------");

            foreach (Student s in students)
            {
                var mine = records.Where(r => r.StudentId.Equals(s.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                Console.WriteLine("{0,-12} {1,-20} {2,-8} {3,-6} {4,-7}",
                    s.Id, s.Name,
                    mine.Count(r => r.Status == "Present"),
                    mine.Count(r => r.Status == "Late"),
                    mine.Count(r => r.Status == "Absent"));
            }
        }
        else
        {
            Console.WriteLine("No attendance data to calculate.");
        }

        Pause();
    }

    // =====================================================
    //  EXPORT
    // =====================================================
    static void ExportAttendance()
    {
        Console.Clear();
        Console.WriteLine("=== EXPORT ATTENDANCE RECORDS ===");

        if (records.Count == 0)
        {
            Console.WriteLine("No attendance records to export.");
            Pause();
            return;
        }

        string? path = GetExportPath();

        if (path == null)
        {
            Pause();
            return;
        }

        try
        {
            List<string> lines = new()
            {
                "Student ID,Student Name,Date,Subject,Class Start,Login Time,Status"
            };

            foreach (AttendanceRecord r in records.OrderBy(r => r.Date).ThenBy(r => r.ClassTime))
            {
                lines.Add(
                    $"{Csv(r.StudentId)},{Csv(GetName(r.StudentId))},{Csv(r.Date)},{Csv(r.Subject)},{Csv(r.ClassTime)},{Csv(r.LoginTime)},{Csv(r.Status)}");
            }

            File.WriteAllLines(path, lines);

            Console.WriteLine("Attendance exported successfully!");
            Console.WriteLine($"Saved to: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Export failed: {ex.Message}");
        }

        Pause();
    }

    static string? GetExportPath()
    {
        Console.Write("Enter file name (without .csv): ");
        string fileName = (Console.ReadLine() ?? "").Trim();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            Console.WriteLine("File name cannot be empty.");
            return null;
        }

        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^4];
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            desktop = Environment.CurrentDirectory;
        }

        return Path.Combine(desktop, fileName + ".csv");
    }

    // =====================================================
    //  PDF EXPORT
    // =====================================================
    static void ExportFullReportPdf()
    {
        Console.Clear();
        Console.WriteLine("=== EXPORT FULL PDF REPORT ===");

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            desktop = Environment.CurrentDirectory;
        }

        string path = Path.Combine(
            desktop,
            $"BSCS1B_Full_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        try
        {
            QuestPDF.Settings.License = LicenseType.Community;

            int present = records.Count(r => r.Status == "Present");
            int late = records.Count(r => r.Status == "Late");
            int absent = records.Count(r => r.Status == "Absent");

            double presentRate = records.Count == 0
                ? 0
                : (double)present / records.Count * 100;

            Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Column(header =>
                    {
                        header.Item().Text("STUDENT ATTENDANCE MONITORING SYSTEM")
                            .FontSize(18).Bold();
                        header.Item().Text("BSCS 1B - Complete Report");
                        header.Item().Text($"Generated: {DateTime.Now:MMMM dd, yyyy hh:mm tt}");
                    });

                    page.Content().Column(content =>
                    {
                        content.Spacing(12);

                        // ---------- 1. STUDENT LIST ----------
                        content.Item().Text("1. STUDENT LIST").FontSize(13).Bold();

                        if (students.Count == 0)
                        {
                            content.Item().Text("No students registered.");
                        }
                        else
                        {
                            content.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.5f);
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(2.5f);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("Student ID");
                                    header.Cell().Element(HeaderCell).Text("Name");
                                    header.Cell().Element(HeaderCell).Text("Registered At");
                                });

                                foreach (Student s in students.OrderBy(s => s.Id))
                                {
                                    table.Cell().Element(BodyCell).Text(s.Id);
                                    table.Cell().Element(BodyCell).Text(s.Name);
                                    table.Cell().Element(BodyCell)
                                        .Text(s.RegisteredAt.ToString("yyyy-MM-dd hh:mm tt"));
                                }
                            });
                        }

                        // ---------- 2. ATTENDANCE RECORDS ----------
                        content.Item().Text("2. ATTENDANCE RECORDS").FontSize(13).Bold();

                        if (records.Count == 0)
                        {
                            content.Item().Text("No attendance records yet.");
                        }
                        else
                        {
                            content.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.5f); // Date
                                    columns.RelativeColumn(1.5f); // Subject
                                    columns.RelativeColumn(1);    // Class start
                                    columns.RelativeColumn(1.5f); // Student ID
                                    columns.RelativeColumn(3);    // Name
                                    columns.RelativeColumn(1.5f); // Login time
                                    columns.RelativeColumn(1);    // Status
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("Date");
                                    header.Cell().Element(HeaderCell).Text("Subject");
                                    header.Cell().Element(HeaderCell).Text("Class Start");
                                    header.Cell().Element(HeaderCell).Text("Student ID");
                                    header.Cell().Element(HeaderCell).Text("Name");
                                    header.Cell().Element(HeaderCell).Text("Login Time");
                                    header.Cell().Element(HeaderCell).Text("Status");
                                });

                                foreach (AttendanceRecord r in records
                                    .OrderBy(r => r.Date).ThenBy(r => r.ClassTime))
                                {
                                    table.Cell().Element(BodyCell).Text(r.Date);
                                    table.Cell().Element(BodyCell).Text(r.Subject);
                                    table.Cell().Element(BodyCell).Text(r.ClassTime);
                                    table.Cell().Element(BodyCell).Text(r.StudentId);
                                    table.Cell().Element(BodyCell).Text(GetName(r.StudentId));
                                    table.Cell().Element(BodyCell).Text(r.LoginTime);
                                    table.Cell().Element(BodyCell).Text(r.Status);
                                }
                            });
                        }

                        // ---------- 3. SUMMARY ----------
                        content.Item().Text("3. ATTENDANCE SUMMARY").FontSize(13).Bold();

                        content.Item().Text($"Registered Students: {students.Count}");
                        content.Item().Text($"Total Attendance Records: {records.Count}");
                        content.Item().Text($"Present: {present}");
                        content.Item().Text($"Late: {late}");
                        content.Item().Text($"Absent: {absent}");
                        content.Item().Text($"Present Rate: {presentRate:F2}%");

                        content.Item().Text("Per-Student Summary").FontSize(11).Bold();

                        if (students.Count == 0)
                        {
                            content.Item().Text("No students registered.");
                        }
                        else
                        {
                            content.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(1.5f);
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(1);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("Student ID");
                                    header.Cell().Element(HeaderCell).Text("Name");
                                    header.Cell().Element(HeaderCell).Text("Present");
                                    header.Cell().Element(HeaderCell).Text("Late");
                                    header.Cell().Element(HeaderCell).Text("Absent");
                                });

                                foreach (Student s in students.OrderBy(s => s.Id))
                                {
                                    var mine = records
                                        .Where(r => r.StudentId.Equals(s.Id, StringComparison.OrdinalIgnoreCase))
                                        .ToList();

                                    table.Cell().Element(BodyCell).Text(s.Id);
                                    table.Cell().Element(BodyCell).Text(s.Name);
                                    table.Cell().Element(BodyCell).Text(mine.Count(r => r.Status == "Present").ToString());
                                    table.Cell().Element(BodyCell).Text(mine.Count(r => r.Status == "Late").ToString());
                                    table.Cell().Element(BodyCell).Text(mine.Count(r => r.Status == "Absent").ToString());
                                }
                            });
                        }
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf(path);

            Console.WriteLine("\nPDF report created successfully!");
            Console.WriteLine($"Saved to: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nCould not create PDF: {ex.Message}");
        }

        Pause();
    }

    static IContainer HeaderCell(IContainer container)
    {
        return container
            .Background(Colors.Grey.Lighten2)
            .Border(0.5f)
            .BorderColor(Colors.Grey.Lighten1)
            .Padding(4)
            .DefaultTextStyle(x => x.Bold());
    }

    static IContainer BodyCell(IContainer container)
    {
        return container
            .BorderBottom(0.5f)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(4);
    }

    // =====================================================
    //  HELPERS
    // =====================================================
    static Student? FindStudent(string id)
    {
        return students.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    static string GetName(string studentId)
    {
        return FindStudent(studentId)?.Name ?? "Unknown Student";
    }

    static string Fmt(TimeSpan t)
    {
        return (DateTime.Today + t).ToString("h:mm tt");
    }

    static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    static void SaveAll()
    {
        SaveData(students, studentFile);
        SaveData(records, attendanceFile);
    }

    static void SaveData<T>(List<T> data, string fileName)
    {
        string json = JsonSerializer.Serialize(
            data,
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(fileName, json);
    }

    static List<T> LoadData<T>(string fileName)
    {
        if (!File.Exists(fileName))
        {
            return new List<T>();
        }

        try
        {
            string json = File.ReadAllText(fileName);
            return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
        }
        catch (JsonException)
        {
            Console.WriteLine($"Warning: Could not read {fileName}.");
            return new List<T>();
        }
    }

    static void Pause()
    {
        Console.WriteLine("\nPress Enter to continue...");
        Console.ReadLine();
    }
}
