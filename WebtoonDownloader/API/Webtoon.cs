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
using WebtoonDownloader.API;

namespace WebtoonDownloader
{
	public struct WebtoonBasicInformation
	{
		public string title;
		public string description;
		public string thumbnailURL;
		public string url;
		public int totalNum;
		public List<WebtoonPageInformation> pages;

		public WebtoonBasicInformation( string title, string description, string thumbnailURL, string url, int totalNum, List<WebtoonPageInformation> pages )
		{
			this.title = title;
			this.description = description;
			this.thumbnailURL = thumbnailURL;
			this.url = url;
			this.totalNum = totalNum;
			this.pages = pages;
		}
	}

	public struct WebtoonPageInformation
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
		public static bool FastDownloadMode;
		public static List<int> DownloadBlockList = new List<int>( );
		public static event Action<string> StatusMessageLabelSet;
		public static event Action<string> ErrorMessageCall;
		public static event Action<bool> DownloadFinished;
		public static event Action<WebtoonPageInformation> DownloadTargetChanged;
		public static event Action<float> DownloadProgressChanged;
		public static Thread DownloadThread = null;


		public static string FixURL( string url )
		{
			try
			{
				Uri newURI = new Uri( url );

				System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString( newURI.Query );

				if ( !string.IsNullOrEmpty( query.Get( "titleId" ) ) )
				{
					if ( url.IndexOf( "page" ) > 0 ) // page 파라메터가 있으면
					{
						query.Remove( "page" ); // 지움
					}

					return newURI.GetLeftPart( UriPartial.Path ) + "?" + query.ToString( );
				}

				return "";
			}
			catch ( UriFormatException )
			{
				return "";
			}
		}

		/*
		public static bool IsValidURL( string url )
		{
			try
			{
				System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString( ( new Uri( url ) ).Query );

				if ( url.IndexOf( "page" ) > 0 ) return false; // &page 방지

				if ( !string.IsNullOrEmpty( query.Get( "titleId" ) ) && string.IsNullOrEmpty( query.Get( "page" ) ) )
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
		*/

		// 해당 웹툰의 최대 리스트 페이지를 반환.
		public static int GetListMaxPage( string url )
		{
			try
			{
				System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo( "en-US" );

				url += "&page=999999999"; // 999999999 로 요청할 시 가장 뒤에 페이지로 이동

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

							foreach ( HtmlNode i in document.DocumentNode.SelectNodes( "//strong" ) )
							{
								if ( i.GetAttributeValue( "class", "" ) == "page" )
								{
									foreach ( HtmlNode node2 in i.ChildNodes )
									{
										if ( node2.OriginalName == "em" && node2.GetAttributeValue( "class", "" ) == "num_page" )
										{
											return int.Parse( node2.InnerText );
										}
									}
								}
							}
						}
					}
				}
			}
			catch ( WebException ex )
			{
				if ( ex.Response == null )
				{
					Utility.WriteErrorLog( ex.Message, "WebException" );
					ErrorMessageCall.Invoke( "귀하의 인터넷 연결을 확인하세요." );
				}
				else
				{
					Utility.WriteErrorLog( ex.Message, "WebException" );
					ErrorMessageCall.Invoke( "서버에서 오류가 발생했습니다, " + ex.Response + " 오류 코드를 반환했습니다." );
				}
			}
			catch ( Exception ex )
			{
				Utility.WriteErrorLog( ex.Message, "Exception" );
				ErrorMessageCall.Invoke( "알 수 없는 오류가 발생했습니다, 로그 파일을 참고하세요." );
			}

			return -1;
		}

		private static List<WebtoonPageInformation> GetTargetPageInformations( string targetPageURL )
		{
			List<WebtoonPageInformation> pages = new List<WebtoonPageInformation>( );

			try
			{
				System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo( "en-US" );

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

											// 기타 데이터 찾기
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
						}
					}
				}
			}
			catch ( WebException ex )
			{
				if ( ex.Response == null )
				{
					Utility.WriteErrorLog( ex.Message, "WebException" );
					ErrorMessageCall.Invoke( "귀하의 인터넷 연결을 확인하세요." );
				}
				else
				{
					Utility.WriteErrorLog( ex.Message, "WebException" );
					ErrorMessageCall.Invoke( "서버에서 오류가 발생했습니다, " + ex.Response + " 오류 코드를 반환했습니다." );
				}
			}
			catch ( Exception ex )
			{
				Utility.WriteErrorLog( ex.Message, "Exception" );
				ErrorMessageCall.Invoke( "알 수 없는 오류가 발생했습니다, 로그 파일을 참고하세요." );
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

						File.WriteAllBytes( directory + @"\image_" + count + ".jpg", ms.ToArray( ) );
					}
				}
			}
		}

		private static async void DownloadImagesTargetURL( WebtoonPageInformation info )
		{
			// 오류
			// 연애혁명 - 130. said, sad <남유리> 에피소드에서 'ArgumentException : 경로에 잘못된 문자가 있습니다' 예외가 발생
			// < > 는 폴더 이름으로 사용할 수 없음.

			// 폴더 이름으로 사용할 수 없는 문자를 제거합니다.
			DownloadProgressChanged.Invoke( 0 );

			info.title = Utility.StripFolderName( info.title );

			string thisDir = Webtoon.BaseDirectory + "\\" + info.num + " - " + info.title;

			StatusMessageLabelSet.Invoke( "다운로드 준비 중 ..." );

			Directory.CreateDirectory( thisDir + "\\이미지" );

			Webtoon.ThumbnailImageDownload( thisDir, info.thumbnailURL );

			List<string> webtoonImages = new List<string>( );

			//try
			//{
				HttpWebRequest request = ( HttpWebRequest ) WebRequest.Create( "http://comic.naver.com" + info.url );
				request.Method = "GET";

				// 인증
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

								StatusMessageLabelSet.Invoke( "다운로드 중 ... " + ( int ) ( ( ( float ) i / ( float ) webtoonImages.Count ) * 100 ) + "%" );
								DownloadProgressChanged.Invoke( ( ( float ) i / ( float ) webtoonImages.Count ) );

								Webtoon.DownloadImageFromDirectURL( thisDir + "\\이미지", url, i );

								if ( !Webtoon.FastDownloadMode )
									Thread.Sleep( 30 );
							}

							await Task.Run( new Action( delegate ( )
							{
								ImageMerge.Merge( thisDir );
							} ) );

							switch ( Viewer.Create( thisDir, info ) )
							{
								case Viewer.ViewerCreateResult.DirectoryNotFoundException:
									ErrorMessageCall.Invoke( info.title + " - 웹툰 뷰어 파일을 생성할 수 없습니다, 저장 폴더를 찾을 수 없습니다." );
									break;
								case Viewer.ViewerCreateResult.IOException:
									ErrorMessageCall.Invoke( info.title + " - 웹툰 뷰어 파일을 생성할 수 없습니다, 파일 처리를 할 수 없었습니다." );
									break;
								case Viewer.ViewerCreateResult.UnauthorizedAccessException:
									ErrorMessageCall.Invoke( info.title + " - 웹툰 뷰어 파일을 생성할 수 없습니다, 액세스가 거부되었습니다." );
									break;
								case Viewer.ViewerCreateResult.Unknown:
									ErrorMessageCall.Invoke( info.title + " - 웹툰 뷰어 파일을 생성할 수 없습니다, 알 수 없는 오류가 발생했습니다, 로그 파일을 참고하세요." );
									break;
								case Viewer.ViewerCreateResult.Success:
									// 뷰어 생성 성공
									break;
							}
						}
					}
				}
			//}
			//catch ( Exception ex )
			//{
			//	Utility.WriteErrorLog( ex.Message, "Exception" );
			//	ErrorMessageCall.Invoke( "알 수 없는 오류가 발생했습니다, 로그 파일을 참고하세요." );
			//}
		}

		public static async Task<WebtoonBasicInformation> GetBasicInformation( string url )
		{
			url = Webtoon.FixURL( url );

			if ( url != "" )
			{
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

							// 인증
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

										StatusMessageLabelSet.Invoke( "기본 정보 불러오는 중 ..." );

										//	특정 웹툰의 설명을 못 가져오는 이유
										//
										//	"이거 나만 그런거 아니지?"
										//	응, 너만 그런거 아니야.
										//	반박불가 공감이 밀려온다.
										//
										//	이러한 설명이 있다고 가정을 하면
										//	HTML 코드의 설명 meta 부분의 정의는
										//	<meta property="og:description" content=""이거 나만 그런거 아니지?"
										//	응, 너만 그런거 아니야.\n반박불가 공감이 밀려온다.">
										//	로 정의된다 하지만 node.GetAttributeValue( "content", "" ) 메소드를 사용하여
										//	content 프로퍼티의 값을 가져오려하면 웹툰 설명에 " 로 시작을 하기 때문에
										//	공백으로 지정된 채 값을 가져오게 됨.

										int descStart = htmlResult.IndexOf( "meta property=\"og:description\"" );

										string[ ] trashDatas = htmlResult.Substring( descStart, htmlResult.IndexOf( ">", descStart ) - descStart )
											.Split( new char[ 1 ] { '"' }, StringSplitOptions.RemoveEmptyEntries );

										string desc = "";

										try
										{
											for ( int i = 3; i < trashDatas.Length; i++ )
											{
												desc += trashDatas[ i ] + Environment.NewLine;
											}
										}
										catch ( IndexOutOfRangeException ) { }

										returnInfo.description = HttpUtility.HtmlDecode( desc.Trim( ) );

										foreach ( HtmlNode node in document.DocumentNode.SelectNodes( "//meta" ) )
										{
											switch ( node.GetAttributeValue( "property", "" ) )
											{
												case "og:title": // 해당 웹툰 이름
													returnInfo.title = node.GetAttributeValue( "content", "" ).Trim( );
													break;
												//case "og:description": // 해당 웹툰 설명 
												//	returnInfo.description = node.GetAttributeValue( "content", "" ).Trim( ); // 일부 웹툰에서 버그가 발생
												//	break;
												case "og:image": // 해당 웹툰 썸네일 이미지
													returnInfo.thumbnailURL = node.GetAttributeValue( "content", "" ).Trim( );
													break;
											}
										}

										List<WebtoonPageInformation> pages = new List<WebtoonPageInformation>( );

										for ( int i = 1; i <= maxPage; i++ )
										{
											List<WebtoonPageInformation> informations = Webtoon.GetTargetPageInformations( url + "&page=" + i );

											pages = pages.Concat( informations ).ToList( );

											StatusMessageLabelSet.Invoke( "페이지 정보 불러오는 중 ... " + ( int ) ( ( ( float ) i / ( float ) maxPage ) * 100 ) + "%" );

											Thread.Sleep( 100 );
										}

										returnInfo.totalNum = pages.Count;
										returnInfo.pages = pages;
									}
								}
							}
						}
						catch ( WebException ex )
						{
							if ( ex.Response == null )
							{
								Utility.WriteErrorLog( ex.Message, "WebException" );
								ErrorMessageCall.Invoke( "귀하의 인터넷 연결을 확인하세요." );
							}
							else
							{
								Utility.WriteErrorLog( ex.Message, "WebException" );
								ErrorMessageCall.Invoke( "서버에서 오류가 발생했습니다, " + ex.Response + " 오류 코드를 반환했습니다." );
							}
						}
						catch ( Exception ex )
						{
							Utility.WriteErrorLog( ex.Message, "Exception" );
							ErrorMessageCall.Invoke( "알 수 없는 오류가 발생했습니다, 로그 파일을 참고하세요." );
						}

						return returnInfo;
					} ) );

					await Task.Delay( 500 );

					return result;
				}
				else
				{
					ErrorMessageCall.Invoke( "입력한 웹툰 링크가 올바르지 않습니다, 올바른 링크를 입력하세요." );
				}
			}
			else
			{
				ErrorMessageCall.Invoke( "입력한 웹툰 링크가 올바르지 않습니다, 올바른 링크를 입력하세요." );
			}

			return new WebtoonBasicInformation( );
		}

		public static void Request( string url )
		{
			DownloadThread = new Thread( ( ) =>
			{
				try
				{
					System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo( "en-US" );

					url = Webtoon.FixURL( url );

					if ( url != "" )
					{
						int maxPage = Webtoon.GetListMaxPage( url );

						if ( maxPage != -1 )
						{
							List<WebtoonPageInformation> pages = new List<WebtoonPageInformation>( );

							for ( int i = 1; i <= maxPage; i++ )
							{
								List<WebtoonPageInformation> informations = Webtoon.GetTargetPageInformations( url + "&page=" + i );

								pages = pages.Concat( informations ).ToList( );

								StatusMessageLabelSet.Invoke( "기본 정보 불러오는 중 ... " + ( int ) ( ( ( float ) i / ( float ) maxPage ) * 100 ) + "%" );

								Thread.Sleep( 100 );
							}

							StatusMessageLabelSet.Invoke( "다운로드 준비 중 ..." );

							foreach ( WebtoonPageInformation info in pages )
							{
								if ( Webtoon.DownloadBlockList.Contains( info.num ) )
								{
									continue;
								}

								DownloadTargetChanged.Invoke( info );
								Webtoon.DownloadImagesTargetURL( info );

								if ( !Webtoon.FastDownloadMode )
									Thread.Sleep( 400 );
							}

							DownloadFinished.Invoke( true );
						}
						else
						{
							DownloadFinished.Invoke( false );
							ErrorMessageCall.Invoke( "입력한 웹툰 링크가 올바르지 않습니다, 올바른 링크를 입력하세요." );
						}
					}
					else
					{
						DownloadFinished.Invoke( false );
						ErrorMessageCall.Invoke( "입력한 웹툰 링크가 올바르지 않습니다, 올바른 링크를 입력하세요." );
					}
				}
				catch ( ThreadAbortException )
				{
					GC.Collect( 0, GCCollectionMode.Forced ); // 쓰레드가 강제 종료된 후 메모리를 정리하기 위해 GC 강제 실행
					Utility.WriteErrorLog( "GC collected.", "ThreadAbortException" );
				}
				catch ( Exception ex )
				{
					Utility.WriteErrorLog( ex.Message, "Exception" );
					ErrorMessageCall.Invoke( "알 수 없는 오류가 발생했습니다, 로그 파일을 참고하세요." );
				}
			} )
			{
				IsBackground = true
			};

			DownloadThread.Start( );
		}
	}
}