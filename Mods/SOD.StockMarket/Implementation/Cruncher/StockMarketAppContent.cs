﻿using SOD.Common;
using SOD.StockMarket.Implementation.Stocks;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UniverseLib;

namespace SOD.StockMarket.Implementation.Cruncher
{
    internal class StockMarketAppContent : CruncherAppContent
    {
        private StockPagination _pagination;
        private StockEntry[] _slots;

        public override void OnSetup()
        {
            // Setup main slots
            InitSlots();

            // Setup pagination
            _pagination = Plugin.Instance.Market.GetPagination();

            // Set exit button listener
            var exitButton = gameObject.transform.FindChild("Exit");
            var button = exitButton.GetComponent<UnityEngine.UI.Button>();
            button.onClick.AddListener(() =>
            {
                controller.OnAppExit();
            });

            // Set next button listener
            var nextButton = gameObject.transform.FindChild("Next");
            button = nextButton.GetComponent<UnityEngine.UI.Button>();
            button.onClick.AddListener(() =>
            {
                Next();
            });

            // Set previous button listener
            var previousButton = gameObject.transform.FindChild("Previous");
            button = previousButton.GetComponent<UnityEngine.UI.Button>();
            button.onClick.AddListener(() =>
            {
                Previous();
            });

            // Set current
            SetSlots(_pagination.Current);

            // Update the current set slots
            Lib.Time.OnMinuteChanged += UpdateStocks;
        }

        private void InitSlots()
        {
            // Setup main slots
            _slots = gameObject.GetComponentsInChildren<RectTransform>()
                .Where(a => a.name.StartsWith("StockEntry"))
                .OrderBy(a => ExtractNumber(a.name))
                .Select(a => new StockEntry(a.gameObject))
                .ToArray();
        }

        private void UpdateStocks(object sender, Common.Helpers.TimeChangedArgs e)
        {
            if (controller == null || !controller.appLoaded) return;

            // Update the stock visuals
            SetSlots(_pagination.Current);
        }

        public void Next()
        {
            var stocks = _pagination.Next();
            SetSlots(stocks);
        }

        public void Previous()
        {
            var stocks = _pagination.Previous();
            SetSlots(stocks);
        }

        private void SetSlots(Stock[] stocks)
        {
            // Initial init
            for (int i = 0; i < stocks.Length; i++)
                _slots[i].SetStock(stocks[i]);

            // When stocks become invalid, (app is closed and re-opened this instance is no longer valid)
            if (_slots.Any(a => a.Invalid))
            {
                Lib.Time.OnMinuteChanged -= UpdateStocks;
                _slots = null;
                _pagination = null;
            }
        }

        private static int ExtractNumber(string name)
        {
            Regex regex = new(@"\d+");
            Match match = regex.Match(name);
            if (match.Success && int.TryParse(match.Value, out int number))
                return number;
            return default;
        }

        private class StockEntry
        {
            internal bool Invalid = false;
            private readonly GameObject _container;
            private readonly TextMeshProUGUI _symbol, _price, _today, _daily, _weekly, _monthly;

            internal StockEntry(GameObject slot)
            {
                _container = slot;
                _symbol = slot.transform.FindChild("Name").GetComponentInChildren<TextMeshProUGUI>();
                _price = slot.transform.FindChild("Price").GetComponentInChildren<TextMeshProUGUI>();
                _today = slot.transform.FindChild("Today").GetComponentInChildren<TextMeshProUGUI>();
                _daily = slot.transform.FindChild("Daily").GetComponentInChildren<TextMeshProUGUI>();
                _weekly = slot.transform.FindChild("Weekly").GetComponentInChildren<TextMeshProUGUI>();
                _monthly = slot.transform.FindChild("Monthly").GetComponentInChildren<TextMeshProUGUI>();
            }

            internal void SetStock(Stock stock)
            {
                if (_container == null)
                {
                    Invalid = true;
                    return;
                }

                if (stock == null)
                {
                    _container.SetActive(false);
                    return;
                }
                _container.SetActive(true);

                // Set symbol and main price
                _symbol.text = stock.Symbol;
                _price.text = stock.Price.ToString(CultureInfo.InvariantCulture);

                // Set price diff of today
                var priceDiffToday = Math.Round(stock.Price - stock.OpeningPrice, 2);
                if (priceDiffToday == 0)
                    _today.color = Color.white;
                else if (priceDiffToday > 0)
                    _today.color = Color.green;
                else
                    _today.color = Color.red;
                _today.text = priceDiffToday.ToString(CultureInfo.InvariantCulture);

                // Set percentages
                SetDailyPercentageText(stock);
                SetWeeklyPercentage(stock);
                SetMonthlyPercentage(stock);
            }

            private void SetDailyPercentageText(Stock stock)
            {
                var dailyPercentage = GetPercentage(stock.Price, stock.OpeningPrice);
                if (dailyPercentage == 0)
                    _daily.color = Color.white;
                else if (dailyPercentage > 0)
                    _daily.color = Color.green;
                else
                    _daily.color = Color.red;
                _daily.text = dailyPercentage.ToString(CultureInfo.InvariantCulture) + " %";
            }

            private void SetWeeklyPercentage(Stock stock)
            {
                var currentDate = Lib.Time.CurrentDate;
                var weekHistorical = stock.HistoricalData
                    .OrderByDescending(a => a.Date)
                    .FirstOrDefault(a => (currentDate - a.Date).TotalDays >= 7);
                if (weekHistorical == null)
                {
                    _weekly.text = "/";
                    _weekly.color = Color.white;
                    return;
                }

                var weeklyPercentage = GetPercentage(stock.Price, weekHistorical.Open);
                if (weeklyPercentage == 0)
                    _weekly.color = Color.white;
                else if (weeklyPercentage > 0)
                    _weekly.color = Color.green;
                else
                    _weekly.color = Color.red;
                _weekly.text = weeklyPercentage.ToString(CultureInfo.InvariantCulture) + " %";
            }

            private void SetMonthlyPercentage(Stock stock)
            {
                var currentDate = Lib.Time.CurrentDate;
                var monthHistorical = stock.HistoricalData
                    .OrderByDescending(a => a.Date)
                    .FirstOrDefault(a => (currentDate - a.Date).TotalDays >= 30);
                if (monthHistorical == null)
                {
                    _monthly.text = "/";
                    _monthly.color = Color.white;
                    return;
                }   
                
                var monthlyPercentage = GetPercentage(stock.Price, monthHistorical.Open);
                if (monthlyPercentage == 0)
                    _monthly.color = Color.white;
                else if (monthlyPercentage > 0)
                    _monthly.color = Color.green;
                else
                    _monthly.color = Color.red;
                _monthly.text = monthlyPercentage.ToString(CultureInfo.InvariantCulture) + " %";
            }

            private static decimal GetPercentage(decimal currentPrice, decimal openingPrice)
            {
                double percentageChange;
                if (openingPrice != 0)
                {
                    percentageChange = (double)((currentPrice - openingPrice) / openingPrice * 100);
                }
                else
                {
                    // Handle the case when openingPrice is zero
                    if (currentPrice > 0)
                    {
                        // If currentPrice is positive, consider percentage change as infinite
                        percentageChange = double.PositiveInfinity;
                    }
                    else if (currentPrice < 0)
                    {
                        // If currentPrice is negative, consider percentage change as negative infinite
                        percentageChange = double.NegativeInfinity;
                    }
                    else
                    {
                        // If currentPrice is also zero, consider percentage change as zero
                        percentageChange = 0;
                    }
                }
                return Math.Round((decimal)percentageChange, 2);
            }
        }
    }
}
