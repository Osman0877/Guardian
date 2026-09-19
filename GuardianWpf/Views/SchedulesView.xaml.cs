using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Guardian.Core.Models;
using Guardian.Core.Services;

namespace GuardianWpf.Views
{
    public partial class SchedulesView : UserControl
    {
        private readonly IDatabaseService _db;
        private readonly IUsageTrackingService _usage;
        private Dictionary<DayOfWeek, AdvancedDaySchedule> _schedules = new();
        private bool _isInitializing = true;

        public SchedulesView(IDatabaseService db, IUsageTrackingService usage)
        {
            InitializeComponent();
            _db = db;
            _usage = usage;

            LoadSchedules();
            _isInitializing = false;
        }

        private void LoadSchedules()
        {
            var list = _db.GetAllAdvancedSchedules();
            _schedules = list.ToDictionary(s => s.Day);

            // Populate all 7 days if missing
            for (int i = 0; i <= 6; i++)
            {
                var day = (DayOfWeek)i;
                if (!_schedules.ContainsKey(day))
                {
                    _schedules[day] = new AdvancedDaySchedule { Day = day, LimitMinutes = 120 };
                }
            }

            DefaultSlider.Value = _schedules[DayOfWeek.Monday].LimitMinutes;
            DefaultMinutesText.Text = FormatMinutes((int)DefaultSlider.Value);

            MonSlider.Value = _schedules[DayOfWeek.Monday].LimitMinutes;
            TueSlider.Value = _schedules[DayOfWeek.Tuesday].LimitMinutes;
            WedSlider.Value = _schedules[DayOfWeek.Wednesday].LimitMinutes;
            ThuSlider.Value = _schedules[DayOfWeek.Thursday].LimitMinutes;
            FriSlider.Value = _schedules[DayOfWeek.Friday].LimitMinutes;
            SatSlider.Value = _schedules[DayOfWeek.Saturday].LimitMinutes;
            SunSlider.Value = _schedules[DayOfWeek.Sunday].LimitMinutes;

            UpdateLabels();
        }

        private void DefaultSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            int mins = (int)DefaultSlider.Value;
            DefaultMinutesText.Text = FormatMinutes(mins);
        }

        private void DaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            UpdateLabels();
        }

        private void UpdateLabels()
        {
            MonText.Text = FormatMinutes((int)MonSlider.Value);
            TueText.Text = FormatMinutes((int)TueSlider.Value);
            WedText.Text = FormatMinutes((int)WedSlider.Value);
            ThuText.Text = FormatMinutes((int)ThuSlider.Value);
            FriText.Text = FormatMinutes((int)FriSlider.Value);
            SatText.Text = FormatMinutes((int)SatSlider.Value);
            SunText.Text = FormatMinutes((int)SunSlider.Value);
        }

        private static string FormatMinutes(int mins)
        {
            int h = mins / 60;
            int m = mins % 60;
            return $"{h}h {m:D2}m ({mins} mins)";
        }

        private void SaveSchedule_Click(object sender, RoutedEventArgs e)
        {
            _schedules[DayOfWeek.Monday].LimitMinutes = (int)MonSlider.Value;
            _schedules[DayOfWeek.Tuesday].LimitMinutes = (int)TueSlider.Value;
            _schedules[DayOfWeek.Wednesday].LimitMinutes = (int)WedSlider.Value;
            _schedules[DayOfWeek.Thursday].LimitMinutes = (int)ThuSlider.Value;
            _schedules[DayOfWeek.Friday].LimitMinutes = (int)FriSlider.Value;
            _schedules[DayOfWeek.Saturday].LimitMinutes = (int)SatSlider.Value;
            _schedules[DayOfWeek.Sunday].LimitMinutes = (int)SunSlider.Value;

            foreach (var s in _schedules.Values)
            {
                _db.SaveAdvancedSchedule(s);
            }

            _usage.RefreshSchedule();

            _db.LogIncident("SCHEDULE_UPDATED", "Schedules", "Parent updated weekly screen-time allowances.");
            MessageBox.Show("Screen-time schedule successfully saved and applied!", "Guardian Schedules", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
