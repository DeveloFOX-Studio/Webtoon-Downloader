﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using WebtoonStoreForm.API;

namespace WebtoonStoreForm
{
	struct WebtoonBasicInformation
	{
		public string title;
		public string description;
		public string thumbnailURL;
		public string url;
		public int totalNum;

		public WebtoonBasicInformation( string title, string description, string thumbnailURL, string url, int totalNum )
		{
			this.title = title;
			this.description = description;
			this.thumbnailURL = thumbnailURL;
			this.url = url;
			this.totalNum = totalNum;
		}
	}

	struct WebtoonPageInformation
	{
		public string title;
		public string thumbnailURL;
		public string url;
		public string starRate;
		public int num;
		public string uploadDate;

		public WebtoonPageInformation( string title, string thumbnailURL, string url, string starRate, int num, string uploadDate )
		{
			this.title = title;
			this.thumbnailURL = thumbnailURL;
			this.url = url;
			this.starRate = starRate;
			this.num = num;
			this.uploadDate = uploadDate;
		}
	}

	static class Webtoon
	{
		public static string BaseDirectory
		{
			get;
			set;
		}
		public static event Action<string> MessageChanged;
		public static event Action<string> ErrorMessageCall;
		public static event Action<bool> RequestStatusChanged;
		public static event Action<WebtoonPageInformation> DownloadTargetChanged;
		public static event Action<float> DownloadProgressChanged;
		public static Thread DownloadThread = null;

		public static bool IsValidURL( string url )
		{
			try
			{
				System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString( ( new Uri( url ) ).Query );

				if ( !string.IsNullOrEmpty( query.Get( "titleId" ) ) && !string.IsNullOrEmpty( query.Get( "weekday" ) ) )
				{
					return true;
				}

				return false;
			}
			catch ( UriFormatException )
			{
				return false;
			}
		}


		// 해당 웹툰의 최대 리스트 페이지를 반환.
		public static int GetListMaxPage( string url )
		{
			url += "&page=999999999"; // 999999999 로 요청할 시 가장 뒤에 페이지로 이동

			try
			{
				HttpWebRequest request = ( HttpWebRequest ) WebRequest.Create( url );
				request.Method = "GET";

				using ( HttpWebResponse response = ( HttpWebResponse ) request.GetResponse( ) )
				{
					using ( Stream responseStream = response.GetResponseStream( ) )
					{
						using ( StreamReader reader = new StreamReader( responseStream, Encoding.UTF8 ) )
						{
							string htmlResult = reader.ReadToEnd( );

							HtmlDocument document = new HtmlDocument( );
							document.LoadHtml( htmlResult );

							//<span class="current">16</span><span class="blind">현재 선택된 페이지</span> <- 찾기
							foreach ( HtmlNode i in document.DocumentNode.SelectNodes( "//span" ) )
							{
								if ( i.GetAttributeValue( "class", "" ) == "current" )
								{
									return int.Parse( i.InnerText );
								}
							}

						}
					}
				}
			}
			catch ( WebException ex )
			{
				Console.WriteLine( "Exception -> " + ex.Message );
			}

			return -1;
		}

		private static List<WebtoonPageInformation> GetTargetPageInformations( string targetPageURL )
		{
			List<WebtoonPageInformation> pages = new List<WebtoonPageInformation>( );

			try
			{
				HttpWebRequest request = ( HttpWebRequest ) WebRequest.Create( targetPageURL );
				request.Method = "GET";

				using ( HttpWebResponse response = ( HttpWebResponse ) request.GetResponse( ) )
				{
					using ( Stream responseStream = response.GetResponseStream( ) )
					{
						using ( StreamReader reader = new StreamReader( responseStream, Encoding.UTF8 ) )
						{
							string htmlResult = reader.ReadToEnd( );

							HtmlDocument document = new HtmlDocument( );
							document.LoadHtml( htmlResult );

							int i = 0; // 현재 리스트의 for문 위치를 체크하기 위함

							foreach ( HtmlNode node in document.DocumentNode.SelectNodes( "//tr" ) )
							{
								if ( node.ChildNodes.Count == 9 ) // 웹툰 리스트의 하위 노드 수는 항상 9개
								{
									foreach ( HtmlNode node2 in node.ChildNodes )
									{
										WebtoonPageInformation info = new WebtoonPageInformation( );

										if ( node2.OriginalName == "td" )
										{
											// 업로드 날짜 정보 찾기

											int tdNodeI = 0;
											foreach ( HtmlNode node3 in document.DocumentNode.SelectNodes( "//td" ) )
											{
												if ( node3.GetAttributeValue( "class", "" ) == "num" )
												{
													if ( tdNodeI == i )
													{
														info.uploadDate = node3.InnerText;
													}

													tdNodeI++;
												}
											}

											// 별점 정보 찾기
											int nodeI = 0; // td 하위 노드의 div 개수를 체크하기 위함

											/* 여기 머리통 깨지는줄; */
											foreach ( HtmlNode node3 in document.DocumentNode.SelectNodes( "//div" ) )
											{
												if ( node3.GetAttributeValue( "class", "" ) == "rating_type" )
												{
													if ( nodeI == i )
													{
														int node2I = 0; // div 하위 노드의 strong 개수를 체크하기 위함

														foreach ( HtmlNode node4 in node3.SelectNodes( "//strong" ) )
														{
															if ( node2I == i ) // 현재 strong for 문 i와 현재 리스트의 i가 같으면 그것이 해당 웹툰 화의 별점 정보
															{
																info.starRate = node4.InnerText;

																break;
															}

															node2I++;
														}
													}

													nodeI++;
												}
											}

											//기타 데이터 찾기
											foreach ( HtmlNode node3 in node2.ChildNodes )
											{
												if ( node3.OriginalName == "a" )
												{
													if ( node3.GetAttributeValue( "href", "" ).StartsWith( "/webtoon/detail.nhn?" ) ) // /webtoon/detail.nhn? 로 시작하는 링크가 해당 웹툰 화 정보의 상위 노드
													{
														foreach ( HtmlNode node4 in node3.ChildNodes )
														{
															if ( node4.OriginalName == "img" )
															{
																info.title = HttpUtility.HtmlDecode( node4.GetAttributeValue( "title", "" ).Trim( ) );
																info.url = node3.GetAttributeValue( "href", "" );

																// 예시 : http://comic.naver.com/webtoon/detail.nhn?titleId=570503&no=151&weekday=thu 이면 no=151 쿼리 정보에서 151이 해당 화의 i임
																info.num = int.Parse( HttpUtility.ParseQueryString( ( new Uri( "http://comic.naver.com" + info.url ) ).Query ).Get( "no" ) );
																info.thumbnailURL = node4.GetAttributeValue( "src", "" );

																break;
															}
														}
													}
												}

											}

											pages.Add( info );
											i++;

											break;
										}
									}
								}
							}






							//foreach ( HtmlNode node in document.DocumentNode.SelectNodes( "//td" ) )
							//{
							//	if ( node.GetAttributeValue( "class", "" ) == "title" )
							//	{
							//		foreach ( HtmlNode node2 in node.ChildNodes )
							//		{
							//			if ( node2.OriginalName == "a" )
							//			{
							//				WebtoonPageInformation info = new WebtoonPageInformation( );

							//				info.title = HttpUtility.HtmlDecode( node2.InnerText );
							//				info.url = node2.GetAttributeValue( "href", "" );
							//				info.num = int.Parse( HttpUtility.ParseQueryString( ( new Uri( "http://comic.naver.com" + info.url ) ).Query ).Get( "no" ) );
							//				info.thumbnailURL = GlobalEnum.blankThumbnailURL; // 초기화;

							//				// 별점 정보 찾기
							//				foreach ( HtmlNode node3 in document.DocumentNode.SelectNodes( "//div" ) )
							//				{
							//					if ( node3.GetAttributeValue( "class", "" ) == "rating_type" )
							//					{
							//						foreach ( HtmlNode node4 in node3.SelectNodes( "//strong" ) )
							//						{
							//							info.starRate = node4.InnerText;

							//							break;
							//						}

							//						break;
							//					}
							//				}

							//				// 썸네일 찾기
							//				foreach ( HtmlNode node3 in document.DocumentNode.SelectNodes( "//a" ) )
							//				{
							//					if ( node3.GetAttributeValue( "href", "" ) == node2.GetAttributeValue( "href", "" ) )
							//					{
							//						foreach ( HtmlNode node4 in node3.ChildNodes )
							//						{
							//							if ( node4.OriginalName == "img" )
							//							{
							//								info.thumbnailURL = node4.GetAttributeValue( "src", GlobalEnum.blankThumbnailURL );
							//								break;
							//							}
							//						}

							//						break;
							//					}
							//				}

							//				pages.Add( info );

							//				break;
							//			}
							//		}
							//	}
							//}
						}
					}
				}
			}
			catch ( WebException ex )
			{
				// Console.WriteLine( "Exception -> " + ex.Message );
			}

			return pages;
		}

		private static void ThumbnailImageDownload( string directory, string directURL )
		{
			HttpWebRequest request = ( HttpWebRequest ) WebRequest.Create( directURL );
			request.Method = "GET";
			request.KeepAlive = true;
			request.Referer = "http://comic.naver.com";
			request.Accept = "image/webp,image/*,*/*;q=0.8";
			request.UserAgent = "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/53.0.2785.116 Safari/537.36";

			using ( HttpWebResponse response = ( HttpWebResponse ) request.GetResponse( ) )
			{
				using ( Stream stReadData = response.GetResponseStream( ) )
				{
					using ( MemoryStream ms = new MemoryStream( ) )
					{
						stReadData.CopyTo( ms );

						File.WriteAllBytes( directory + @"\썸네일.jpg", ms.ToArray( ) );
					}
				}
			}
		}

		private static void DownloadImageFromDirectURL( string directory, string directURL, int count )
		{
			HttpWebRequest request = ( HttpWebRequest ) WebRequest.Create( directURL );
			request.Method = "GET";
			request.KeepAlive = true;
			request.Referer = "http://comic.naver.com";
			request.Accept = "image/webp,image/*,*/*;q=0.8";
			request.UserAgent = "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/53.0.2785.116 Safari/537.36";

			using ( HttpWebResponse response = ( HttpWebResponse ) request.GetResponse( ) )
			{
				using ( Stream stReadData = response.GetResponseStream( ) )
				{
					using ( MemoryStream ms = new MemoryStream( ) )
					{
						stReadData.CopyTo( ms );

						File.WriteAllBytes( directory + @"\image_" + count + ".png", ms.ToArray( ) );
					}
				}
			}
		}

		private static void DownloadImagesTargetURL( WebtoonPageInformation info )
		{
			// 오류
			// 연애혁명 - 130. said, sad <남유리> 에피소드에서 'ArgumentException : 경로에 잘못된 문자가 있습니다' 예외가 발생
			// < > 는 폴더 이름으로 사용할 수 없음.

			// 폴더 이름으로 사용할 수 없는 문자를 제거합니다.
			DownloadProgressChanged.Invoke( 0 );

			info.title = Utility.StripFolderName( info.title );

			string thisDir = Webtoon.BaseDirectory + "\\" + info.num + " - " + info.title;

			MessageChanged.Invoke( "다운로드 준비 ... " + info.title );

			//Directory.CreateDirectory( thisDir );
			Directory.CreateDirectory( thisDir + "\\이미지" );

			Webtoon.ThumbnailImageDownload( thisDir, info.thumbnailURL );

			List<string> webtoonImages = new List<string>( );

			try
			{
				HttpWebRequest request = ( HttpWebRequest ) WebRequest.Create( "http://comic.naver.com" + info.url );
				request.Method = "GET";
				//request.CookieContainer = new CookieContainer( );
				//foreach ( CookieStruct i in rr )
				//{
				//	request.CookieContainer.Add( new Cookie( i.id, i.value, "/", request.Host ) );
				//}

				using ( HttpWebResponse response = ( HttpWebResponse ) request.GetResponse( ) )
				{
					using ( Stream responseStream = response.GetResponseStream( ) )
					{
						using ( StreamReader reader = new StreamReader( responseStream, Encoding.UTF8 ) )
						{
							string htmlResult = reader.ReadToEnd( );

							HtmlDocument document = new HtmlDocument( );
							document.LoadHtml( htmlResult );

							foreach ( HtmlNode node in document.DocumentNode.SelectNodes( "//img" ) )
							{
								if ( node.GetAttributeValue( "src", "" ).StartsWith( "http://imgcomic.naver.net/webtoon/" ) )
								{
									webtoonImages.Add( node.GetAttributeValue( "src", "" ) );
								}
							}

							int i = 0;

							foreach ( string url in webtoonImages )
							{
								i++;

								MessageChanged.Invoke( "다운로드 중 ... " + ( int ) ( ( ( float ) i / ( float ) webtoonImages.Count ) * 100 ) + "%" );
								DownloadProgressChanged.Invoke( ( ( float ) i / ( float ) webtoonImages.Count ) );

								Webtoon.DownloadImageFromDirectURL( thisDir + "\\이미지", url, i );

								System.Threading.Thread.Sleep( 50 );
							}

							ImageMerge.Merge( thisDir );
							Viewer.Create( thisDir, info );
						}
					}
				}
			}
			catch ( Exception )
			{

			}
		}



		public static async Task<WebtoonBasicInformation> GetBasicInformation( string url )
		{
			if ( Webtoon.IsValidURL( url ) )
			{
				//RequestButtonStatusChanged.Invoke( false );

				int maxPage = Webtoon.GetListMaxPage( url );

				if ( maxPage != -1 )
				{
					WebtoonBasicInformation result = await Task.Run<WebtoonBasicInformation>( new Func<WebtoonBasicInformation>( delegate ( )
					{
						WebtoonBasicInformation returnInfo = new WebtoonBasicInformation( );
						returnInfo.url = url;

						try
						{
							HttpWebRequest request = ( HttpWebRequest ) WebRequest.Create( url );
							request.Method = "GET";
							//request.CookieContainer = new CookieContainer( );
							//foreach ( CookieStruct i in rr )
							//{
							//	request.CookieContainer.Add( new Cookie( i.id, i.value, "/", request.Host ) );
							//}

							using ( HttpWebResponse response = ( HttpWebResponse ) request.GetResponse( ) )
							{
								using ( Stream responseStream = response.GetResponseStream( ) )
								{
									using ( StreamReader reader = new StreamReader( responseStream, Encoding.UTF8 ) )
									{
										string htmlResult = reader.ReadToEnd( );

										HtmlDocument document = new HtmlDocument( );
										document.LoadHtml( htmlResult );

										foreach ( HtmlNode node in document.DocumentNode.SelectNodes( "//meta" ) )
										{
											switch ( node.GetAttributeValue( "property", "" ) )
											{
												case "og:title": // 해당 웹툰 이름
													returnInfo.title = node.GetAttributeValue( "content", "" ).Trim( );
													break;
												case "og:description": // 해당 웹툰 설명
													returnInfo.description = node.GetAttributeValue( "content", "" ).Trim( );
													break;
												case "og:image": // 해당 웹툰 썸네일 이미지
													returnInfo.thumbnailURL = node.GetAttributeValue( "content", "" ).Trim( );
													break;
											}
										}
									}
								}
							}
						}
						catch ( Exception ex )
						{
							ErrorMessageCall.Invoke( "알 수 없는 오류가 발생했습니다.\n\n" + ex.Message );
						}
						finally
						{
							//RequestButtonStatusChanged.Invoke( true );
						}

						return returnInfo;
					} ) );

					await Task.Delay( 1000 );

					return result;
				}
				else
				{
					ErrorMessageCall.Invoke( "올바른 URL 을 입력하세요 :)" );
				}
			}
			else
			{
				ErrorMessageCall.Invoke( "올바른 URL 을 입력하세요." );
			}

			return new WebtoonBasicInformation( );
		}

		public static void Request( string url )
		{
			DownloadThread = new Thread( ( ) =>
			{
				try
				{
					if ( Webtoon.IsValidURL( url ) )
					{
						int maxPage = Webtoon.GetListMaxPage( url );

						if ( maxPage != -1 )
						{
							RequestStatusChanged.Invoke( true );

							List<WebtoonPageInformation> pages = new List<WebtoonPageInformation>( );

							for ( int i = 1; i <= maxPage; i++ )
							{
								List<WebtoonPageInformation> informations = Webtoon.GetTargetPageInformations( url + "&page=" + i );

								pages = pages.Concat( informations ).ToList( );

								MessageChanged.Invoke( "페이지 정보 불러오는 중 ...  -> " + i + " / " + maxPage );

								Thread.Sleep( 100 );
							}

							MessageChanged.Invoke( "다운로드 하고 있습니다 ..." );

							foreach ( WebtoonPageInformation info in pages )
							{
								DownloadTargetChanged.Invoke( info );
								Webtoon.DownloadImagesTargetURL( info );

								Thread.Sleep( 3000 );
							}

							RequestStatusChanged.Invoke( false );
						}
						else
						{
							MessageChanged.Invoke( "올바른 URL 을 입력하세요 :)" );
						}
					}
					else
					{
						MessageChanged.Invoke( "올바른 URL 을 입력하세요." );
					}
				}
				catch ( ThreadAbortException ex )
				{
					ErrorMessageCall.Invoke( "쓰레드 인터럽트" );

					GC.Collect( 0, GCCollectionMode.Forced );
				}
				//catch ( Exception ex )
				//{
				//	ErrorMessageCall.Invoke( "알 수 없는 오류가 발생했습니다.\n\n" + ex.Message );
				//}
			} )
			{
				IsBackground = true
			};

			DownloadThread.Start( );
		}
	}
}
