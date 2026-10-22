# UltimateKtv

KTV 點歌與播放系統：使用者在主畫面、手機或 YouTube 點歌，歌曲依序在電視上播放。

## 點歌與待播

**待播清單 (Waiting list)**：
已點但尚未播放的歌曲，依播放順序排列；正在播放的那首不在其中。
_Avoid_: 歌單、播放清單、playlist、queue

**點歌 (Order)**：
把一首歌加到待播清單的最後面。同一首歌（同檔案，或同歌名加同歌手）不能同時在待播清單中出現兩次。
_Avoid_: 加歌、add

**插播 (Insert first)**：
把一首歌放到待播清單的第一首；若它已在清單中，改為移到第一首。
_Avoid_: 優先、置頂點歌

**點歌者 (Ordered by)**：
點這首歌的人或來源名稱，顯示在待播清單與跑馬燈上。
_Avoid_: 使用者、requester

**點歌來源 (Order source)**：
點歌是從哪裡發起的：主畫面 (User)、手機遠端 (Remote)、YouTube 下載完成 (YouTube)、隨機播放 (Random)。
_Avoid_: isRemoteCall

## 播放

**播放場次 (Playback session)**：
從待播清單一首接一首播放的整個過程，包含正在播放的歌與閒置狀態；決定何時開播下一首。
_Avoid_: 播放器、player（player 指播放影片的引擎）

**正在播放 (Now playing)**：
目前由待播清單取出、在電視上播放的那一首歌。
_Avoid_: current song、playing song data

**閒置 (Idle)**：
待播清單為空且沒有正在播放的歌。
_Avoid_: 停止、stopped

**切歌 (Skip)**：
結束正在播放的歌，立即播放待播清單的下一首。
_Avoid_: 下一首、next

**重唱 (Replay)**：
正在播放的歌從頭再播一次。
_Avoid_: 重播、restart

**單曲循環 (Loop)**：
正在播放的歌播完後自動重唱，而不是播下一首。
_Avoid_: 重複播放、repeat

**隨機歌 (Random song)**：
閒置時由系統自動點的歌；使用者一點歌就會被切掉，且不計播放次數。
_Avoid_: 自動播放歌曲

**播放次數 (Play count)**：
一首歌被唱過的次數；播放進度超過設定比例才算一次，隨機歌不算。
_Avoid_: 點播次數

**人聲 / 伴唱 (Vocal / Music)**：
同一首歌的兩種聲音版本：有原唱人聲，或只有伴奏。
_Avoid_: 聲道、音軌（那是影片檔內部的實作方式）

**固定人聲 (Fixed vocal)**：
使用者指定之後每首歌開播都直接播人聲，直到按人聲或伴唱鈕解除。
_Avoid_: 鎖定音軌

## 電視畫面

**電視畫面 (TV screen)**：
給唱歌的人看的影片畫面；單螢幕時與主控台共用同一個螢幕。
_Avoid_: 播放視窗、第二螢幕、PlayerScreen 以外的說法

**跑馬燈 (Marquee)**：
疊在電視畫面或主控台上的文字訊息，可以捲動或靜態顯示，例如播放中、點歌成功、公告、角落提示。
_Avoid_: 字幕、通知列

**電視疊加層 (TV overlay)**：
把跑馬燈與歌詞疊在電視畫面影片上的那一層。
_Avoid_: OSD（那是 libmpv 的實作方式）

## 歌庫

**歌庫 (Song library)**：
所有本機歌曲及其資料（播放次數、音量、伴唱位置），加上我的最愛與點唱紀錄。
_Avoid_: 資料庫、DB、歌單

**我的最愛 (Favorites)**：
使用者收藏的歌曲清單，每位使用者一份。
_Avoid_: 收藏、書籤

**今日點唱 (Today's list)**：
這次開機以來唱過的歌；第一首歌開播時清空。
_Avoid_: 播放紀錄

**點唱紀錄 (Play history)**：
依開機時間 (MM/dd HH:mm) 分組保存的唱過歌曲，只保留最近設定次數的開機。
_Avoid_: 歷史清單
