﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grpc.Core;
using v2ray.Core.App.Stats.Command;
using x2tap.Properties;
using x2tap.Utils;
using x2tap.View.Server;

namespace x2tap.View
{
    public partial class MainForm : Form
    {
        /// <summary>
        ///     流量
        /// </summary>
        public long Bandwidth;

        /// <summary>
        ///     启动状态
        /// </summary>
        public bool Started;

        /// <summary>
        ///     状态信息
        /// </summary>
        public string Status = "请下达命令！";

        public MainForm()
        {
            InitializeComponent();

			CheckForIllegalCrossThreadCalls = false;
        }

        /// <summary>
        ///     初始化代理
        /// </summary>
        public void InitProxies()
        {
            // 先清空掉内容
            ProxyComboBox.Items.Clear();
            // 添加 v2ray 代理
            foreach (var v2ray in Global.v2rayProxies)
            {
                ProxyComboBox.Items.Add(string.Format("[v2ray] {0}", v2ray.Remark));
            }

            // 添加 Shadowsocks 代理
            foreach (var shadowsocks in Global.ShadowsocksProxies)
            {
                ProxyComboBox.Items.Add(string.Format("[Shadowsocks] {0}", shadowsocks.Remark));
            }

			// 添加 ShadowsocksR 代理
			foreach (var shadowsocksr in Global.ShadowsocksRProxies)
			{
				ProxyComboBox.Items.Add(string.Format("[ShadowsocksR] {0}", shadowsocksr.Remark));
			}

            if (ProxyComboBox.Items.Count > 0)
            {
                ProxyComboBox.SelectedIndex = 0;
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // 初始化日志目录
            if (!Directory.Exists("logging"))
            {
                Directory.CreateDirectory("logging");
            }

            // 初始化配置
            Config.InitFromFile();

            // 初始化代理
            InitProxies();
            if (ProxyComboBox.Items.Count > 0)
            {
                ProxyComboBox.SelectedIndex = 0;
            }

            // 初始化模式
            ModeComboBox.SelectedIndex = 0;
			foreach (var mode in Global.Modes)
			{
				ModeComboBox.Items.Add(string.Format("[外置规则] {0}", mode.Name));
			}

            // 初始化适配器
            Task.Run(() =>
            {
                using (var client = new UdpClient("114.114.114.114", 53))
                {
                    var address = ((IPEndPoint) client.Client.LocalEndPoint).Address;
                    Global.Config.adapterAddress = address.ToString();

                    var addressGeted = false;

                    var adapters = NetworkInterface.GetAllNetworkInterfaces();
                    foreach (var adapter in adapters)
                    {
                        var properties = adapter.GetIPProperties();

                        foreach (var information in properties.UnicastAddresses)
                        {
                            if (information.Address.AddressFamily == AddressFamily.InterNetwork && Equals(information.Address, address))
                            {
                                addressGeted = true;
                            }
                        }

                        foreach (var information in properties.GatewayAddresses)
                        {
                            if (addressGeted)
                            {
                                Global.Config.adapterGateway = information.Address.ToString();
                                break;
                            }
                        }

                        if (addressGeted)
                        {
                            break;
                        }
                    }
                }
            });

            // 后台工作
            Task.Run(() =>
            {
				var count = 0;

				while (true)
                {
                    try
                    {
						// 更新标题栏时间
						Text = string.Format("x2tap - {0}", DateTime.Now.ToString());

						// 更新状态信息
						StatusLabel.Text = string.Format("状态：{0}", Status);
						
						// 更新流量信息
						if (Started)
                        {
							if (count % 10 == 0)
							{
								Task.Run(() =>
								{
									var channel = new Channel("127.0.0.1:2811", ChannelCredentials.Insecure);
									var asyncTask = channel.ConnectAsync();

									asyncTask.Wait(100);
									if (asyncTask.IsCompleted)
									{
										// 创建客户端实例
										var client = new StatsService.StatsServiceClient(channel);

										// 获取并重置 上行/下行 统计信息
										var uplink = client.GetStats(new GetStatsRequest { Name = "inbound>>>defaultInbound>>>traffic>>>uplink", Reset = true });
										var downlink = client.GetStats(new GetStatsRequest { Name = "inbound>>>defaultInbound>>>traffic>>>downlink", Reset = true });

										// 加入总流量
										Bandwidth += uplink.Stat.Value;
										Bandwidth += downlink.Stat.Value;

										// 更新流量信息
										UsedBandwidthLabel.Text = $"已使用：{Util.ComputeBandwidth(Bandwidth)}";
										UplinkSpeedLabel.Text = $"↑：{Util.ComputeBandwidth(uplink.Stat.Value)}/s";
										DownlinkSpeedLabel.Text = $"↓：{Util.ComputeBandwidth(downlink.Stat.Value)}/s";
									}
								});
							}
                        }
                        else
                        {
                            Bandwidth = 0;
                            UsedBandwidthLabel.Text = "已使用：0 KB";
                            UplinkSpeedLabel.Text = "↑：0 KB/s";
                            DownlinkSpeedLabel.Text = "↓：0 KB/s";
                        }

						if (count > 100000)
						{
							count = 0;
						}
						else
						{
							count++;
						}

                        // 休眠 100 毫秒
                        Thread.Sleep(100);
                    }
                    catch (Exception)
                    {
                        // 跳过
                    }
                }
            });
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Started)
            {
                e.Cancel = true;

                MessageBox.Show("请先点击关闭按钮", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                Config.SaveToFile();
				Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "x2tap.exe");
            }
        }

        private void ComboBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            var cbx = sender as ComboBox;
            if (cbx != null)
            {
                e.DrawBackground();

                if (e.Index >= 0)
                {
                    var sf = new StringFormat();
                    sf.LineAlignment = StringAlignment.Center;
                    sf.Alignment = StringAlignment.Center;

                    var brush = new SolidBrush(cbx.ForeColor);

                    if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
                    {
                        brush = SystemBrushes.HighlightText as SolidBrush;
                    }

                    e.Graphics.DrawString(cbx.Items[e.Index].ToString(), cbx.Font, brush, e.Bounds, sf);
                }
            }
        }

        private void Addv2rayServerButton_Click(object sender, EventArgs e)
        {
            (Global.Views.Server.v2ray = new Server.v2ray()).Show();
            Hide();
        }

        private void AddShadowsocksServerButton_Click(object sender, EventArgs e)
        {
            (Global.Views.Server.Shadowsocks = new Shadowsocks()).Show();
            Hide();
        }

		private void AddShadowsocksRServerButton_Click(object sender, EventArgs e)
		{
			(Global.Views.Server.ShadowsocksR = new ShadowsocksR()).Show();
			Hide();
		}

		private void DeleteButton_Click(object sender, EventArgs e)
        {
            var index = ProxyComboBox.SelectedIndex;
            if (index != -1)
            {
                ProxyComboBox.Items.RemoveAt(index);

                if (index < Global.v2rayProxies.Count)
                {
                    Global.v2rayProxies.RemoveAt(index);
                }
                else if (index < Global.v2rayProxies.Count + Global.ShadowsocksProxies.Count)
                {
                    Global.ShadowsocksProxies.RemoveAt(index - Global.v2rayProxies.Count);
                }
				else
				{
					Global.ShadowsocksRProxies.RemoveAt(index - Global.v2rayProxies.Count - Global.ShadowsocksProxies.Count);
				}

                if (ProxyComboBox.Items.Count < index)
                {
                    ProxyComboBox.SelectedIndex = index;
                }
                else if (ProxyComboBox.Items.Count == 1)
                {
                    ProxyComboBox.SelectedIndex = 0;
                }
                else
                {
                    ProxyComboBox.SelectedIndex = index - 1;
                }
            }
            else
            {
                MessageBox.Show("请选择一个代理", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void EditButton_Click(object sender, EventArgs e)
        {
            if (ProxyComboBox.SelectedIndex != -1)
            {
                if (ProxyComboBox.SelectedIndex < Global.v2rayProxies.Count)
                {
                    (Global.Views.Server.v2ray = new Server.v2ray(true, ProxyComboBox.SelectedIndex)).Show();
                }
                else if (ProxyComboBox.SelectedIndex < Global.v2rayProxies.Count)
                {
                    (Global.Views.Server.Shadowsocks = new Shadowsocks(true, ProxyComboBox.SelectedIndex - Global.v2rayProxies.Count)).Show();
                }
				else
				{
					(Global.Views.Server.ShadowsocksR = new ShadowsocksR(true, ProxyComboBox.SelectedIndex - Global.v2rayProxies.Count - Global.ShadowsocksProxies.Count)).Show();
				}

                Hide();
            }
            else
            {
                MessageBox.Show("请选择一个代理", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SubscribeButton_Click(object sender, EventArgs e)
        {
            (Global.Views.SubscribeForm = new SubscribeForm()).Show();
            Hide();
        }

        private void AdvancedButton_Click(object sender, EventArgs e)
        {
            (Global.Views.AdvancedForm = new AdvancedForm()).Show();
            Hide();
        }

        private void ControlButton_Click(object sender, EventArgs e)
        {
            if (!Started)
            {
                if (ProxyComboBox.SelectedIndex != -1)
                {
                    if (TUNTAP.GetComponentId() != "")
                    {
                        Status = "执行中";
						Reset(false);
                        ControlButton.Text = "执行中";

                        Task.Run(() =>
                        {
							try
							{
								Thread.Sleep(1000);
								Status = "正在生成配置文件中";
								if (ModeComboBox.SelectedIndex == 0)
								{
									File.WriteAllText("v2ray.txt", ProxyComboBox.Text.StartsWith("[v2ray]") ? Config.v2rayGet(Global.v2rayProxies[ProxyComboBox.SelectedIndex]) : Config.ShadowsocksGet(Global.ShadowsocksProxies[ProxyComboBox.SelectedIndex - Global.v2rayProxies.Count]));
								}
								else
								{
									File.WriteAllText("v2ray.txt", ProxyComboBox.Text.StartsWith("[v2ray]") ? Config.v2rayGet(Global.v2rayProxies[ProxyComboBox.SelectedIndex], false) : Config.ShadowsocksGet(Global.ShadowsocksProxies[ProxyComboBox.SelectedIndex - Global.v2rayProxies.Count], false));
								}

								Thread.Sleep(1000);
								Status = "正在启动 v2ray 中";
								Shell.ExecuteCommandNoWait("start", "wv2ray.exe", "-config", "v2ray.txt");

								Thread.Sleep(2000);
								try
								{
									using (var client = new TcpClient())
									{
										var task = client.BeginConnect("127.0.0.1", 2810, null, null);
										if (!task.AsyncWaitHandle.WaitOne(1000))
										{
											throw new TimeoutException();
										}

										client.EndConnect(task);
									}
								}
								catch (Exception)
								{
									Status = "检测到 v2ray 启动失败";
									Reset();
									Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "wv2ray.exe");
									MessageBox.Show("检测到 v2ray 启动失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
									return;
								}

								Thread.Sleep(1000);
								Status = "正在启动 tun2socks 中";
								Shell.ExecuteCommandNoWait("start", "RunHiddenConsole.exe", "tun2socks.exe", "-enable-dns-cache", "-local-socks-addr", "127.0.0.1:2810", "-tun-address", "10.0.236.10", "-tun-mask", "255.255.255.0", "-tun-gw", "10.0.236.1", "-tun-dns", "127.0.0.1");

								Thread.Sleep(2000);
								if (Process.GetProcessesByName("tun2socks").Length == 0)
								{
									Status = "检测到 tun2socks 启动失败";
									Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "tun2socks.exe");
									MessageBox.Show("检测到 tun2socks 启动失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
									return;
								}

								Thread.Sleep(1000);
								Status = "正在配置 路由表 中";
								if (ModeComboBox.SelectedIndex == 0 || ModeComboBox.SelectedIndex == 1)
								{
									if (!Route.Add("0.0.0.0", "0.0.0.0", "10.0.236.1"))
									{
										Route.Delete("0.0.0.0", "0.0.0.0", "10.0.236.1");
										Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "wv2ray.exe");
										Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "tun2socks.exe");
										Status = "在操作路由表时发生错误！";
										Reset();
										MessageBox.Show("在操作路由表时发生错误！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
										return;
									}

									if (!Route.Add("0.0.0.0", "128.0.0.0", "10.0.236.1"))
									{
										Route.Delete("0.0.0.0", "0.0.0.0", "10.0.236.1");
										Route.Delete("0.0.0.0", "128.0.0.0", "10.0.236.1");
										Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "wv2ray.exe");
										Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "tun2socks.exe");
										Status = "在操作路由表时发生错误！";
										Reset();
										MessageBox.Show("在操作路由表时发生错误！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
										return;
									}
								}
								else
								{
									var mode = Global.Modes[ModeComboBox.SelectedIndex - 2];
									if (mode.Type == 1)
									{
										if (!Route.Add("0.0.0.0", "0.0.0.0", "10.0.236.1"))
										{
											Route.Delete("0.0.0.0", "0.0.0.0", "10.0.236.1");
											Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "wv2ray.exe");
											Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "tun2socks.exe");
											Status = "在操作路由表时发生错误！";
											Reset();
											MessageBox.Show("在操作路由表时发生错误！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
											return;
										}

										if (!Route.Add("0.0.0.0", "128.0.0.0", "10.0.236.1"))
										{
											Route.Delete("0.0.0.0", "0.0.0.0", "10.0.236.1");
											Route.Delete("0.0.0.0", "128.0.0.0", "10.0.236.1");
											Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "wv2ray.exe");
											Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "tun2socks.exe");
											Status = "在操作路由表时发生错误！";
											Reset();
											MessageBox.Show("在操作路由表时发生错误！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
											return;
										}
									}

									foreach (var rule in mode.Rule)
									{
										var splited = rule.Split('/');
										if (splited.Length == 2)
										{
											if (mode.Type == 0)
											{
												Route.Add(splited[0], Route.TranslateCIDR(splited[1]), "10.0.236.1");
											}
											else
											{
												Route.Add(splited[0], Route.TranslateCIDR(splited[1]), Global.Config.adapterGateway);
											}
										}
									}
								}

								Thread.Sleep(1000);
								Status = "正在清理 DNS 缓存中";
								Shell.ExecuteCommandNoWait("ipconfig", "/flushdns");

								Thread.Sleep(1000);
								Status = "已启动，请自行检查网络是否正常";
								Bandwidth = 0;
								Started = true;
								ControlButton.Text = "停止";
								ControlButton.Enabled = true;
							}
                            catch (Exception ex)
							{
								Reset(true);

								MessageBox.Show(ex.ToString(), "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
							}
                        });
                    }
                    else
                    {
                        MessageBox.Show("未检测到 TUN/TAP 适配器，请检查 TAP-Windows 驱动是否正确安装！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("请选择一个代理", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                ControlButton.Text = "执行中";
                ControlButton.Enabled = false;

				Task.Run(() =>
				{
					Thread.Sleep(1000);
					Status = "正在重置 路由表 中";
					Route.Delete("0.0.0.0", "0.0.0.0", "10.0.236.1");
					Route.Delete("0.0.0.0", "128.0.0.0", "10.0.236.1");
					if (ModeComboBox.SelectedIndex != 0 && ModeComboBox.SelectedIndex != 1)
					{
						foreach (var rule in Global.Modes[ModeComboBox.SelectedIndex - 2].Rule)
						{
							var splited = rule.Split('/');
							if (splited.Length == 2)
							{
								Route.Delete(splited[0]);
							}
						}
					}

					Thread.Sleep(1000);
					Status = "正在停止 tun2socks 中";
					Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "wv2ray.exe");

					Thread.Sleep(1000);
					Status = "正在停止 v2ray 中";
					Shell.ExecuteCommandNoWait("taskkill", "/f", "/t", "/im", "tun2socks.exe");

					Status = "已停止";
					Started = false;
					Reset();
				});
            }
        }

        private void ProjectLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Shell.ExecuteCommandNoWait("start", "https://github.com/hacking001/x2tap");
        }

		private void Reset(bool type = true)
		{
			ProxyComboBox.Enabled = type;
			ModeComboBox.Enabled = type;
			Addv2rayServerButton.Enabled = type;
			AddShadowsocksServerButton.Enabled = type;
			DeleteButton.Enabled = type;
			EditButton.Enabled = type;
			SubscribeButton.Enabled = type;
			AdvancedButton.Enabled = type;
			ControlButton.Text = "启动";
			ControlButton.Enabled = type;
		}
	}
}