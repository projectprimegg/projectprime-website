import test from "node:test";
import assert from "node:assert/strict";
import {youtubeVideoId,replaySharePath,replayShareTitle} from "../lib/replayShare.ts";

test("only valid HTTPS YouTube links or exact IDs are eligible",()=>{
  const id="dQw4w9WgXcQ";
  for(const value of [id,"https://youtu.be/"+id,
    "https://www.youtube.com/watch?v="+id+"&t=8",
    "https://m.youtube.com/shorts/"+id,
    "https://www.youtube-nocookie.com/embed/"+id,
    "https://youtube.com/live/"+id]) assert.equal(youtubeVideoId(value),id);
  for(const value of ["http://youtube.com/watch?v="+id,
    "https://youtube.com.evil.test/watch?v="+id,
    "https://user:pass@youtube.com/watch?v="+id,
    "https://youtube.com:8443/watch?v="+id,
    "file:///home/user/private.ppdemo","/Users/me/replay.ppdemo",
    "https://evil.test/watch?v="+id,"bad","https://youtu.be/"+id+"/extra"])
    assert.equal(youtubeVideoId(value),null,value);
});
test("share paths include only safe video identity and bounded title",()=>{
  assert.equal(replaySharePath("https://youtu.be/dQw4w9WgXcQ"," My  match\n highlight "),
    "/watch/replays?video=dQw4w9WgXcQ&title=My+match+highlight");
  assert.equal(replaySharePath("/tmp/secret.ppdemo"),null);
  assert.equal(replayShareTitle(" test\t "), "test");
  assert.equal(replaySharePath("dQw4w9WgXcQ","a".repeat(200))?.split("title=")[1]?.length,100);
});
